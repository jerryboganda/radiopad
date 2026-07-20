using System.Reflection;
using Xunit;

namespace RadioPad.Api.Tests.Infrastructure;

/// <summary>
/// Guard for the invariant behind the 2026-07 flaky-test hunt: process environment is global and
/// xUnit runs test collections in parallel, so ANY test class that mutates env vars must live in a
/// collection defined with <c>DisableParallelization = true</c> — otherwise it races every
/// concurrently running class that reads that var (directly, or ambiently through a hosted API's
/// middleware/controllers), producing exactly the once-in-several-runs failure signature that was
/// observed once and never reproduced.
///
/// <para>The 2026-07-20 audit found ~20 env-mutating test classes outside any parallel-disabled
/// collection, including three files touching <c>STRIPE_WEBHOOK_SECRET</c> with only partial
/// coverage. All were serialized; this test keeps the invariant from regressing — a new
/// env-mutating test fails HERE with instructions instead of becoming the next unnamed flake.</para>
///
/// <para>Source scanning is deliberate: reflection cannot see who calls
/// <c>SetEnvironmentVariable</c>. A token in a comment produces a false positive, but the required
/// annotation is harmless, so the trade is acceptable.</para>
/// </summary>
public class EnvSerializationConventionTests
{
    /// <summary>Call shapes that mutate process env in this test project (helpers included).</summary>
    private static readonly string[] MutationTokens =
    {
        "SetEnvironmentVariable(",
        "EnvVarScope(",
        "EnvScope(",
    };

    [Fact]
    public void Every_Env_Mutating_Test_Class_Is_In_A_Parallel_Disabled_Collection()
    {
        var sourceRoot = FindTestProjectRoot();
        var asm = typeof(EnvSerializationConventionTests).Assembly;

        // Collections that actually stop the parallel scheduler.
        var serialized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in asm.GetTypes())
        {
            foreach (var cad in type.GetCustomAttributesData())
            {
                if (cad.AttributeType != typeof(CollectionDefinitionAttribute)) continue;
                var name = cad.ConstructorArguments.Count > 0 ? cad.ConstructorArguments[0].Value as string : null;
                var disabled = cad.NamedArguments.Any(a =>
                    a.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization) &&
                    a.TypedValue.Value is true);
                if (name is not null && disabled) serialized.Add(name);
            }
        }
        Assert.NotEmpty(serialized); // at minimum EnvironmentVariableCollection must exist

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file);
            if (!MutationTokens.Any(text.Contains)) continue;

            foreach (var (className, body) in TopLevelClassSegments(text))
            {
                // This class quotes the tokens as string literals; it mutates nothing.
                if (className == nameof(EnvSerializationConventionTests)) continue;
                if (!MutationTokens.Any(body.Contains)) continue;

                foreach (var type in asm.GetTypes().Where(t => t.Name == className && !t.IsNested))
                {
                    // Non-test classes (EnvVarScope, SttSmokeGate, …) are scoped restore helpers,
                    // not schedulable units — the classes USING them are what must serialize.
                    // Custom attributes like [EnvFact] derive from FactAttribute, so this sees them.
                    var isTestClass = type.GetMethods()
                        .Any(m => m.GetCustomAttributes(inherit: true).OfType<FactAttribute>().Any());
                    if (!isTestClass) continue;

                    var collection = type.GetCustomAttributesData()
                        .Where(a => a.AttributeType == typeof(CollectionAttribute))
                        .Select(a => a.ConstructorArguments.Count > 0 ? a.ConstructorArguments[0].Value as string : null)
                        .FirstOrDefault(n => n is not null);

                    if (collection is null || !serialized.Contains(collection))
                        violations.Add(
                            $"{Path.GetRelativePath(sourceRoot, file)}: {className} " +
                            $"(collection: {collection ?? "none"})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "These test classes mutate process environment but are not in a parallel-disabled " +
            "collection, so they can race the rest of the suite (the known flaky-test mechanism). " +
            "Add [Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)] " +
            "to each (or move it into another collection whose [CollectionDefinition] sets " +
            "DisableParallelization = true):\n  " + string.Join("\n  ", violations));
    }

    /// <summary>
    /// Walk up from the test binaries to the test project source. Hard-fails when absent: this
    /// guard running against nothing must never read as a pass (same stance as SttSmokeGate).
    /// </summary>
    private static string FindTestProjectRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RadioPad.Api.Tests.csproj")))
                return dir.FullName;
        }
        throw new InvalidOperationException(
            "RadioPad.Api.Tests.csproj not found above " + AppContext.BaseDirectory +
            " — the env-serialization convention cannot be checked without the test sources.");
    }

    /// <summary>
    /// Split a source file into top-level class segments: (class name, text up to the next
    /// top-level class). Nested private helpers are indented, so their content folds into the
    /// enclosing test class's segment — which is the attribution we want.
    ///
    /// <para>Known blind spot: the column-0 anchor assumes file-scoped namespaces (every test
    /// file today). A block-scoped namespace would indent its classes and this scan would miss
    /// them — if that style ever appears in the test project, teach this parser first.</para>
    /// </summary>
    private static IEnumerable<(string Name, string Body)> TopLevelClassSegments(string text)
    {
        var lines = text.Split('\n');
        var decls = new List<(int Line, string Name)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                lines[i],
                @"^(?:public|internal)\s+(?:(?:sealed|static|abstract|partial)\s+)*class\s+(\w+)");
            if (m.Success) decls.Add((i, m.Groups[1].Value));
        }
        for (var d = 0; d < decls.Count; d++)
        {
            var end = d + 1 < decls.Count ? decls[d + 1].Line : lines.Length;
            yield return (decls[d].Name, string.Join("\n", lines[decls[d].Line..end]));
        }
    }
}
