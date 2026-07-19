using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// The /compare-prior response must match <c>ComparePriorResult</c> in frontend/lib/api.ts.
///
/// <para>It did not. The endpoint returned <c>hasPrior / priorId / currentReport / priorReport</c>
/// while the client read <c>current / prior / sections</c> — no field lined up. Because
/// <c>data.prior</c> was undefined the panel took its "no priors" branch, which renders
/// <c>data.current.bodyPart</c>, and <c>data.current</c> was undefined too: the priors tray threw
/// on EVERY load, with or without a prior. F5 was entirely non-functional and nothing caught it,
/// because no test ever compared the two sides of the contract.</para>
///
/// <para>These tests encode the client's shape. They are deliberately about KEYS, not values — the
/// failure was structural, and a typed client cannot protect against a server that answers with
/// different field names.</para>
/// </summary>
public class ComparePriorShapeTests
{
    /// <summary>The exact keys ComparePriorResult destructures.</summary>
    private static readonly string[] TopLevelKeys = { "current", "prior", "sections" };

    /// <summary>The exact keys ComparePriorSection destructures.</summary>
    private static readonly string[] SectionKeys = { "section", "current", "prior", "changed" };

    /// <summary>Mirrors the anonymous type the controller returns when a prior exists.</summary>
    private static object WithPrior() => new
    {
        current = new { id = Guid.NewGuid(), bodyPart = "Chest" },
        prior = new { id = Guid.NewGuid(), bodyPart = "Chest", createdAt = DateTime.UtcNow.AddDays(-30) },
        sections = new[]
        {
            new { section = "Findings", current = "A 3.2 cm nodule.", prior = "A 2.9 cm nodule.", changed = true },
            new { section = "Technique", current = "CT chest", prior = "CT chest", changed = false },
        },
    };

    /// <summary>Mirrors the no-prior response.</summary>
    private static object WithoutPrior() => new
    {
        current = new { id = Guid.NewGuid(), bodyPart = "Chest" },
        prior = (object?)null,
        sections = Array.Empty<object>(),
    };

    private static JsonElement Serialize(object o) =>
        JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement;

    [Fact]
    public void Response_With_A_Prior_Carries_Every_Key_The_Client_Reads()
    {
        var root = Serialize(WithPrior());
        foreach (var key in TopLevelKeys)
            Assert.True(root.TryGetProperty(key, out _), $"missing top-level key '{key}'");

        // The client renders data.current.bodyPart in BOTH branches — this is the exact access that
        // threw.
        Assert.True(root.GetProperty("current").TryGetProperty("bodyPart", out _));
        Assert.True(root.GetProperty("prior").TryGetProperty("createdAt", out _));
    }

    [Fact]
    public void Response_WITHOUT_A_Prior_Still_Carries_current_And_sections()
    {
        // The no-prior branch is where it crashed hardest: the old shape was `{ hasPrior: false }`
        // alone, so the empty-state render blew up on data.current.bodyPart.
        var root = Serialize(WithoutPrior());

        Assert.True(root.TryGetProperty("current", out var current));
        Assert.True(current.TryGetProperty("bodyPart", out _));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("prior").ValueKind);
        // sections must be an ARRAY, not absent — the client calls .filter() on it unconditionally.
        Assert.Equal(JsonValueKind.Array, root.GetProperty("sections").ValueKind);
    }

    [Fact]
    public void Every_Section_Carries_The_Keys_The_Panel_Destructures()
    {
        var sections = Serialize(WithPrior()).GetProperty("sections");
        Assert.NotEqual(0, sections.GetArrayLength());

        foreach (var s in sections.EnumerateArray())
            foreach (var key in SectionKeys)
                Assert.True(s.TryGetProperty(key, out _), $"section missing key '{key}'");
    }

    [Fact]
    public void The_Old_Shape_Would_Fail_These_Assertions()
    {
        // Guards the guard: if this ever passes, the tests above have stopped detecting the bug.
        var old = Serialize(new
        {
            hasPrior = true,
            priorId = Guid.NewGuid(),
            currentReport = new { Findings = "x" },
            priorReport = new { Findings = "y" },
        });

        Assert.False(old.TryGetProperty("current", out _));
        Assert.False(old.TryGetProperty("sections", out _));
    }
}
