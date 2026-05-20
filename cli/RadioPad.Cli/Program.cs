using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RadioPad.Cli.Commands;
using RadioPad.Domain.Entities;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("RadioPad CLI — radiology reporting and governance toolkit");

        // CLI-010 — global headless flag. When set, every command refuses to
        // prompt and exits non-zero on missing config / unauthenticated state.
        var headlessOpt = new Option<bool>("--headless", "Non-interactive mode: no prompts, exit non-zero on missing config.");
        root.AddGlobalOption(headlessOpt);
        root.AddValidator(r =>
        {
            CliRuntime.Headless = r.GetValueForOption(headlessOpt);
        });

        root.AddCommand(BuildLoginCommand());
        root.AddCommand(BuildDaemonCommand());
        root.AddCommand(BuildRulebookCommand());
        root.AddCommand(BuildReportCommand());
        root.AddCommand(BuildGenerateCommand());
        root.AddCommand(BuildAuditCommand());
        root.AddCommand(BuildProviderCommand());
        root.AddCommand(BuildTemplatesCommand());
        root.AddCommand(BuildPacksCommand());
        root.AddCommand(BuildIngestCommand());
        root.AddCommand(BuildDicomCommand());
        root.AddCommand(BuildPluginCommand());
        root.AddCommand(BuildBundleCommand());
        root.AddCommand(BuildPacsCommand());

        // PRD §17.4 — read-only MCP server.
        var mcp = new Command("mcp", "Model Context Protocol server (stdio)");
        var serve = new Command("serve", "Start a JSON-RPC 2.0 MCP server over stdio.");
        serve.SetHandler(async () => Environment.Exit(await McpServer.RunAsync()));
        mcp.AddCommand(serve);
        root.AddCommand(mcp);

        // PRD §17.6 — model evaluation harness. Runs golden-case fixtures
        // against a deployed provider and reports a pass-rate.
        var eval = new Command("eval", "Run model evaluation against golden cases");
        var dirArg = new Argument<DirectoryInfo>("dir", "Directory of golden case JSON files");
        eval.AddArgument(dirArg);
        eval.SetHandler(async (DirectoryInfo dir) =>
        {
            int total = 0, passed = 0;
            foreach (var file in dir.EnumerateFiles("*.json", SearchOption.AllDirectories))
            {
                total++;
                try
                {
                    var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName));
                    var root = doc.RootElement;
                    var report = root.GetProperty("report").GetRawText();
                    _ = ReadExpectedFindingIds(root);
                    var http = NewHttpClient();
                    var resp = await http.PostAsync("/api/reports/golden-case",
                        new StringContent(report, Encoding.UTF8, "application/json"));
                    if (resp.IsSuccessStatusCode) passed++;
                    Console.WriteLine($"{(resp.IsSuccessStatusCode ? "PASS" : "FAIL")}  {file.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL  {file.Name}  ({ex.Message})");
                }
            }
            Console.WriteLine($"\n{passed}/{total} passed");
            Environment.Exit(passed == total ? 0 : 1);
        }, dirArg);
        root.AddCommand(eval);

        return await root.InvokeAsync(args);
    }

    // ------------------------------------------------------------------ login
    private static Command BuildLoginCommand()
    {
        var cmd = new Command("login", "Configure tenant + user identity used by the CLI");
        var tenant = new Option<string>("--tenant", () => "dev", "Tenant slug");
        var user = new Option<string>("--user", () => "radiologist@radiopad.local", "User email");
        var server = new Option<string>("--server", () => "http://127.0.0.1:7457", "Backend base URL");
        var deviceFlow = new Option<bool>("--device-flow", "Run RFC 8628 device authorization flow and store the access token");
        cmd.AddOption(tenant);
        cmd.AddOption(user);
        cmd.AddOption(server);
        cmd.AddOption(deviceFlow);
        cmd.SetHandler(async (string t, string u, string sv, bool df) =>
        {
            if (df)
            {
                var rc = await DeviceFlow.RunAsync(t, u, sv, default);
                Environment.Exit(rc);
                return;
            }
            CliRuntime.SaveConfig(new CliRuntime.CliConfig(t, u, sv, null));
            Console.WriteLine($"Saved identity: tenant={t}, user={u}, server={sv}");
        }, tenant, user, server, deviceFlow);
        return cmd;
    }

    // ----------------------------------------------------------------- daemon
    private static Command BuildDaemonCommand()
    {
        var cmd = new Command("daemon", "Manage the local RadioPad backend service");
        var bindOpt = new Option<string>("--bind", () => "127.0.0.1", "Address to bind (default 127.0.0.1)");
        var portOpt = new Option<int>("--port", () => 7457, "TCP port to listen on (default 7457)");

        var status = new Command("status", "Print whether the local daemon is running");
        status.SetHandler(() => Environment.Exit(DaemonControl.Status()));

        var start = new Command("start", "Spawn the radiopad-api binary as a detached process");
        start.AddOption(bindOpt);
        start.AddOption(portOpt);
        start.SetHandler((string b, int p) => Environment.Exit(DaemonControl.Start(b, p)), bindOpt, portOpt);

        var stop = new Command("stop", "Gracefully shut down the local daemon (5s grace, then hard kill)");
        stop.SetHandler(() => Environment.Exit(DaemonControl.Stop()));

        var restart = new Command("restart", "Stop and start the local daemon");
        restart.AddOption(bindOpt);
        restart.AddOption(portOpt);
        restart.SetHandler((string b, int p) => Environment.Exit(DaemonControl.Restart(b, p)), bindOpt, portOpt);

        cmd.AddCommand(status);
        cmd.AddCommand(start);
        cmd.AddCommand(stop);
        cmd.AddCommand(restart);
        return cmd;
    }

    // --------------------------------------------------------------- rulebook
    private static Command BuildRulebookCommand()
    {
        var cmd = new Command("rulebook", "Validate and test rulebooks");
        var fileArg = new Argument<FileInfo>("file", "YAML rulebook file");

        var validate = new Command("validate", "Lint a rulebook YAML file");
        validate.AddArgument(fileArg);
        validate.SetHandler((FileInfo file) =>
        {
            var yaml = File.ReadAllText(file.FullName);
            try
            {
                var rb = RulebookSpec.FromYaml(yaml);
                Console.WriteLine($"OK — {rb.RulebookId} v{rb.Version}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"INVALID — {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileArg);

        cmd.AddCommand(validate);
        cmd.AddCommand(BuildRulebookTestCommand());
        cmd.AddCommand(BuildRulebookApproveCommand());
        return cmd;
    }

    private static Command BuildRulebookTestCommand()
    {
        var cmd = new Command("test", "Run golden-case fixtures against a rulebook");
        var fileArg = new Argument<FileInfo>("file", "YAML rulebook file");
        var casesOpt = new Option<DirectoryInfo>("--cases", "Directory containing *.json case files") { IsRequired = true };
        cmd.AddArgument(fileArg);
        cmd.AddOption(casesOpt);
        cmd.SetHandler((FileInfo file, DirectoryInfo cases) =>
        {
            var spec = RulebookSpec.FromYaml(File.ReadAllText(file.FullName));
            var validator = new ReportValidator();
            int passed = 0, total = 0;
            foreach (var caseFile in cases.GetFiles("*.json"))
            {
                total++;
                using var s = caseFile.OpenRead();
                var doc = JsonDocument.Parse(s);
                var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? caseFile.Name : caseFile.Name;
                var report = ParseReport(doc.RootElement.GetProperty("report"));
                var expected = ReadExpectedFindingIds(doc.RootElement);
                var v = validator.Validate(report, spec);
                var actual = v.Findings.Select(f => f.RuleId).Distinct().ToArray();
                var missing = expected.Except(actual).ToArray();
                var unexpected = actual.Except(expected).ToArray();
                var ok = missing.Length == 0 && unexpected.Length == 0;
                if (ok) passed++;
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
                if (!ok)
                {
                    if (missing.Length > 0) Console.WriteLine($"      missing:    {string.Join(", ", missing)}");
                    if (unexpected.Length > 0) Console.WriteLine($"      unexpected: {string.Join(", ", unexpected)}");
                }
            }
            Console.WriteLine();
            Console.WriteLine($"{passed}/{total} cases passed");
            if (passed != total) Environment.ExitCode = 1;
        }, fileArg, casesOpt);
        return cmd;
    }

    static string[] ReadExpectedFindingIds(JsonElement root)
    {
        if (root.TryGetProperty("expectFlagged", out var current) && current.ValueKind == JsonValueKind.Array)
            return current.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (root.TryGetProperty("expectedFindings", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
            return legacy.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        return Array.Empty<string>();
    }

    private static Report ParseReport(JsonElement el)
    {
        var r = new Report();
        if (el.TryGetProperty("study", out var st))
        {
            r.Study.Modality = st.TryGetProperty("modality", out var m) ? m.GetString() ?? "" : "";
            r.Study.BodyPart = st.TryGetProperty("bodyPart", out var b) ? b.GetString() ?? "" : "";
            r.Study.Indication = st.TryGetProperty("indication", out var i) ? i.GetString() ?? "" : "";
            r.Study.AccessionNumber = st.TryGetProperty("accessionNumber", out var a) ? a.GetString() ?? "" : "";
        }
        r.Indication = el.TryGetProperty("indication", out var ind) ? ind.GetString() ?? "" : "";
        r.Technique = el.TryGetProperty("technique", out var tq) ? tq.GetString() ?? "" : "";
        r.Comparison = el.TryGetProperty("comparison", out var cm) ? cm.GetString() ?? "" : "";
        r.Findings = el.TryGetProperty("findings", out var fn) ? fn.GetString() ?? "" : "";
        r.Impression = el.TryGetProperty("impression", out var ip) ? ip.GetString() ?? "" : "";
        r.Recommendations = el.TryGetProperty("recommendations", out var rc) ? rc.GetString() ?? "" : "";
        return r;
    }

    private static Command BuildRulebookApproveCommand()
    {
        var cmd = new Command("approve", "Promote a rulebook to Approved status");
        var idOpt = new Option<string>("--id", "Rulebook GUID") { IsRequired = true };
        cmd.AddOption(idOpt);
        cmd.SetHandler(async (string id) =>
        {
            using var http = NewHttpClient();
            var resp = await http.PostAsync($"/api/rulebooks/{id}/approve", null);
            Console.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.WriteLine(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode) Environment.ExitCode = 1;
        }, idOpt);
        return cmd;
    }

    // --------------------------------------------------------------- generate
    private static Command BuildGenerateCommand()
    {
        var cmd = new Command("generate", "Generate a draft / impression / rewrite for a study");
        var report = new Option<string?>("--report", "Existing report id (omit to create a new draft from --input)");
        var providerOpt = new Option<string?>("--provider", "Provider id (defaults to auto-routed compliant provider)");
        var modeOpt = new Option<string>("--mode", () => "impression",
            "AI mode: draft | impression | cleanup | concise | formal | patient_friendly | referring_summary");
        var rulebookOpt = new Option<string?>("--rulebook", "Rulebook id or GUID (informational; the API selects by modality + body part)");
        var templateOpt = new Option<string?>("--template", "Template id or GUID — when --report is omitted, a new draft is created bound to this template");
        var inputOpt = new Option<FileInfo?>("--input", "Local findings file (txt/md). Required when --report is omitted; ignored otherwise.");
        var outputOpt = new Option<string?>("--out", "Output file (use '-' for stdout). Alias of --output.");
        var output2Opt = new Option<string?>("--output", "Output file (use '-' for stdout)");
        var formatOpt = new Option<string>("--format", () => "json").FromAmong("json", "text");
        cmd.AddOption(report);
        cmd.AddOption(providerOpt);
        cmd.AddOption(modeOpt);
        cmd.AddOption(rulebookOpt);
        cmd.AddOption(templateOpt);
        cmd.AddOption(inputOpt);
        cmd.AddOption(outputOpt);
        cmd.AddOption(output2Opt);
        cmd.AddOption(formatOpt);
        cmd.SetHandler(async (System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var r = ctx.ParseResult.GetValueForOption(report);
            var p = ctx.ParseResult.GetValueForOption(providerOpt);
            var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? "impression";
            var rulebook = ctx.ParseResult.GetValueForOption(rulebookOpt);
            var template = ctx.ParseResult.GetValueForOption(templateOpt);
            var input = ctx.ParseResult.GetValueForOption(inputOpt);
            var output = ctx.ParseResult.GetValueForOption(outputOpt)
                         ?? ctx.ParseResult.GetValueForOption(output2Opt);
            var format = ctx.ParseResult.GetValueForOption(formatOpt) ?? "json";

            using var http = NewHttpClient();
            Guid? providerGuid = Guid.TryParse(p, out var g) ? g : null;

            // CLI-003 — local-input flow: create a new draft report bound to
            // --template, seed Findings/Indication from --input, then route
            // through the same /api/reports/{id}/ai pipeline as the existing
            // --report flow. Honours PHI guard locally before any provider call.
            if (string.IsNullOrEmpty(r))
            {
                if (input is null || !input.Exists)
                {
                    Console.Error.WriteLine("--input <file> is required when --report is omitted.");
                    Environment.Exit(CliRuntime.ExitInvalidInput);
                    return;
                }
                var findings = await File.ReadAllTextAsync(input.FullName);
                var (modality, bodyPart, templateId) = await ResolveTemplateAsync(http, template);
                var createBody = new Dictionary<string, object?>
                {
                    ["modality"] = modality,
                    ["bodyPart"] = bodyPart,
                    ["accessionNumber"] = $"CLI-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
                    ["templateId"] = templateId,
                    ["rulebookId"] = Guid.TryParse(rulebook, out var rgid) ? (Guid?)rgid : null,
                };
                var createResp = await http.PostAsJsonAsync("/api/reports", createBody);
                if (!createResp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"{(int)createResp.StatusCode} {createResp.ReasonPhrase}");
                    Console.Error.WriteLine(await createResp.Content.ReadAsStringAsync());
                    Environment.Exit(CliRuntime.ExitFailure);
                    return;
                }
                using var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
                r = createDoc.RootElement.GetProperty("id").GetString();

                // PATCH findings + indication so the AI gateway has source
                // text to work with for "draft"/"cleanup"/"impression" modes.
                var patchBody = new Dictionary<string, object?>
                {
                    ["findings"] = findings,
                };
                using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/reports/{r}")
                {
                    Content = JsonContent.Create(patchBody),
                };
                var patchResp = await http.SendAsync(patchReq);
                if (!patchResp.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"{(int)patchResp.StatusCode} {patchResp.ReasonPhrase}");
                    Console.Error.WriteLine(await patchResp.Content.ReadAsStringAsync());
                    Environment.Exit(CliRuntime.ExitFailure);
                    return;
                }
            }

            // CLI-008 — defence-in-depth client-side PHI guard.
            var guardRc = await PhiGuard.EnsureAllowedAsync(http, r!, providerGuid, default);
            if (guardRc != 0) Environment.Exit(guardRc);

            var body = new Dictionary<string, object?>
            {
                ["mode"] = mode,
                ["providerId"] = providerGuid,
                ["rulebookId"] = rulebook,
            };
            var resp = await http.PostAsJsonAsync($"/api/reports/{r}/ai", body);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.ReasonPhrase}");
                Console.Error.WriteLine(raw);
                Environment.Exit(CliRuntime.ExitFailure);
            }

            string rendered = raw;
            if (format == "text")
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    rendered = doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                        ? t.GetString() ?? raw
                        : raw;
                }
                catch
                {
                    rendered = raw;
                }
            }
            else
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    rendered = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                catch
                {
                    rendered = raw;
                }
            }

            if (string.IsNullOrEmpty(output) || output == "-")
            {
                Console.WriteLine(rendered);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, rendered);
                Console.WriteLine($"saved {output}");
            }
        });
        return cmd;
    }

    /// <summary>
    /// CLI-003 — resolves a template id (stable snake_case) or row GUID to
    /// the GUID + modality + body part used when creating a fresh report.
    /// </summary>
    private static async Task<(string modality, string bodyPart, Guid? templateId)> ResolveTemplateAsync(HttpClient http, string? idOrTemplateId)
    {
        if (string.IsNullOrEmpty(idOrTemplateId)) return ("", "", null);
        var json = await http.GetStringAsync("/api/templates");
        using var doc = JsonDocument.Parse(json);
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            var tid = t.TryGetProperty("templateId", out var ti) ? ti.GetString() ?? "" : "";
            var rowId = t.TryGetProperty("id", out var rIdElem) ? rIdElem.GetString() ?? "" : "";
            if (string.Equals(tid, idOrTemplateId, StringComparison.OrdinalIgnoreCase)
             || string.Equals(rowId, idOrTemplateId, StringComparison.OrdinalIgnoreCase))
            {
                var modality = t.TryGetProperty("modality", out var m) ? m.GetString() ?? "" : "";
                var bodyPart = t.TryGetProperty("bodyPart", out var b) ? b.GetString() ?? "" : "";
                return (modality, bodyPart, Guid.TryParse(rowId, out var gid) ? gid : null);
            }
        }
        return ("", "", null);
    }

    // ----------------------------------------------------------------- report
    private static Command BuildReportCommand()
    {
        var cmd = new Command("report", "Inspect and operate on individual reports");

        var list = new Command("list", "Show recent reports for the active tenant");
        list.SetHandler(async () =>
        {
            using var http = NewHttpClient();
            var json = await http.GetStringAsync("/api/reports");
            using var doc = JsonDocument.Parse(json);
            Console.WriteLine($"{"id",-38}  {"acc",-12}  {"modality",-8}  {"body",-12}  status");
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var id = r.GetProperty("id").GetString() ?? "";
                var acc = r.GetProperty("study").GetProperty("accessionNumber").GetString() ?? "";
                var mod = r.GetProperty("study").GetProperty("modality").GetString() ?? "";
                var body = r.GetProperty("study").GetProperty("bodyPart").GetString() ?? "";
                var st = r.TryGetProperty("status", out var s) ? s.ToString() : "";
                Console.WriteLine($"{id,-38}  {acc,-12}  {mod,-8}  {body,-12}  {st}");
            }
        });

        var get = new Command("get", "Print a report as JSON");
        var idOpt = new Option<string>("--id", "Report id") { IsRequired = true };
        get.AddOption(idOpt);
        get.SetHandler(async (string id) =>
        {
            using var http = NewHttpClient();
            Console.WriteLine(await http.GetStringAsync($"/api/reports/{id}"));
        }, idOpt);

        var validate = new Command("validate", "Run rulebook validation against a report");
        validate.AddOption(idOpt);
        validate.SetHandler(async (string id) =>
        {
            using var http = NewHttpClient();
            var resp = await http.PostAsync($"/api/reports/{id}/validate", null);
            Console.WriteLine(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode) Environment.ExitCode = 1;
        }, idOpt);

        var export = new Command("export", "Export a report (text or fhir)");
        var format = new Option<string>("--format", () => "text").FromAmong("text", "fhir");
        export.AddOption(idOpt);
        export.AddOption(format);
        export.SetHandler(async (string id, string fmt) =>
        {
            using var http = NewHttpClient();
            var path = fmt == "fhir" ? "export/fhir" : "export/text";
            Console.WriteLine(await http.GetStringAsync($"/api/reports/{id}/{path}"));
        }, idOpt, format);

        cmd.AddCommand(list);
        cmd.AddCommand(get);
        cmd.AddCommand(validate);
        cmd.AddCommand(export);
        return cmd;
    }

    // ------------------------------------------------------------------ audit
    private static Command BuildAuditCommand()
    {
        var cmd = new Command("audit", "Query the tenant audit log");
        var export = new Command("export", "Print recent audit events as JSON");
        var take = new Option<int>("--take", () => 100);
        export.AddOption(take);
        export.SetHandler(async (int n) =>
        {
            using var http = NewHttpClient();
            var json = await http.GetStringAsync($"/api/audit?take={n}");
            Console.WriteLine(json);
        }, take);

        var verify = new Command("verify", "Recompute the SHA-256 audit hash chain locally and report breaks");
        var verifyTake = new Option<int>("--take", () => 1000);
        verify.AddOption(verifyTake);
        verify.SetHandler(async (int n) =>
        {
            using var http = NewHttpClient();
            var json = await http.GetStringAsync($"/api/audit?take={n}");
            using var doc = JsonDocument.Parse(json);
            var events = new List<JsonElement>();
            foreach (var ev in doc.RootElement.EnumerateArray()) events.Add(ev);
            events.Reverse();
            var prev = "";
            int ok = 0, breaks = 0;
            foreach (var ev in events)
            {
                var id = ev.GetProperty("id").GetString() ?? "";
                var tenantId = ev.GetProperty("tenantId").GetString() ?? "";
                var action = ev.GetProperty("action").GetInt32();
                var details = ev.GetProperty("detailsJson").GetString() ?? "";
                var stored = ev.GetProperty("integrityChain").GetString() ?? "";
                var canonical = $"{id}|{tenantId}|{action}|{details}|{prev}";
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
                if (string.Equals(hash, stored, StringComparison.OrdinalIgnoreCase))
                {
                    ok++;
                }
                else
                {
                    breaks++;
                    Console.Error.WriteLine($"BROKEN  {id}  expected={hash}  stored={stored}");
                }
                prev = stored;
            }
            Console.WriteLine($"verified {ok} ok, {breaks} broken");
            if (breaks > 0) Environment.ExitCode = 1;
        }, verifyTake);

        cmd.AddCommand(export);
        cmd.AddCommand(verify);

        // CLI-009 — audit sync to local NDJSON.
        var sync = new Command("sync", "Pull new audit events to a local NDJSON file (resumable)");
        var outOpt = new Option<FileInfo?>("--out", "Destination NDJSON file (default ~/.radiopad/audit-events.ndjson)");
        var fromOpt = new Option<DateTimeOffset?>("--from", "Inclusive start (ISO-8601)");
        var toOpt = new Option<DateTimeOffset?>("--to", "Inclusive end (ISO-8601)");
        sync.AddOption(outOpt);
        sync.AddOption(fromOpt);
        sync.AddOption(toOpt);
        sync.SetHandler(async (FileInfo? outFile, DateTimeOffset? from, DateTimeOffset? to) =>
        {
            Environment.Exit(await AuditSync.RunAsync(outFile, from, to, default));
        }, outOpt, fromOpt, toOpt);
        cmd.AddCommand(sync);
        return cmd;
    }

    // ----------------------------------------------------------------- providers
    private static Command BuildProviderCommand()
    {
        var cmd = new Command("provider", "Inspect AI providers (no secret material)");
        var list = new Command("list", "Print configured providers");
        list.SetHandler(async () =>
        {
            using var http = NewHttpClient();
            Console.WriteLine(await http.GetStringAsync("/api/providers"));
        });

        var test = new Command("test", "Round-trip a provider against a temporary report");
        var idOpt = new Option<string>("--id", "Provider id (guid)") { IsRequired = true };
        test.AddOption(idOpt);
        test.SetHandler(async (string providerId) =>
        {
            using var http = NewHttpClient();
            // Create throwaway draft report, then ask the provider for an impression.
            var create = await http.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "CLI provider smoke test",
                accessionNumber = $"CLI-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            });
            create.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
            var reportId = doc.RootElement.GetProperty("id").GetString();
            try
            {
                var resp = await http.PostAsJsonAsync($"/api/reports/{reportId}/ai", new
                {
                    mode = "impression",
                    providerId = Guid.Parse(providerId),
                });
                Console.WriteLine($"status: {(int)resp.StatusCode} {resp.StatusCode}");
                Console.WriteLine(await resp.Content.ReadAsStringAsync());
                if (!resp.IsSuccessStatusCode) Environment.ExitCode = 1;
            }
            finally
            {
                // best-effort cleanup is the operator's choice; we leave the
                // report in Draft so the audit trail is preserved.
            }
        }, idOpt);

        cmd.AddCommand(list);
        cmd.AddCommand(test);

        // CLI-007 — provider register from the CLI.
        var register = new Command("register", "Register a new AI provider via POST /api/providers");
        var typeOpt = new Option<string>("--type", "Adapter type") { IsRequired = true };
        typeOpt.FromAmong(ProviderRegister.SupportedTypes);
        var nameOpt = new Option<string>("--name", "Display name") { IsRequired = true };
        var baseUrlOpt = new Option<string?>("--base-url", "Endpoint URL (optional for openai/anthropic/ollama/mock)");
        var modelOpt = new Option<string>("--model", "Model id") { IsRequired = true };
        var keyOpt = new Option<string?>("--api-key-ref", "Secret reference (env:<NAME>) when the provider requires one");
        register.AddOption(typeOpt);
        register.AddOption(nameOpt);
        register.AddOption(baseUrlOpt);
        register.AddOption(modelOpt);
        register.AddOption(keyOpt);
        register.SetHandler(async (string type, string name, string? baseUrl, string model, string? keyRef) =>
        {
            Environment.Exit(await ProviderRegister.RegisterAsync(type, name, baseUrl, model, keyRef, default));
        }, typeOpt, nameOpt, baseUrlOpt, modelOpt, keyOpt);
        cmd.AddCommand(register);
        return cmd;
    }

    // -------------------------------------------------------------- templates
    private static Command BuildTemplatesCommand()
    {
        var cmd = new Command("templates", "Manage report templates");
        var list = new Command("list", "List templates");
        list.SetHandler(async () => Environment.Exit(await TemplatesCommands.ListAsync(default)));

        var export = new Command("export", "Export a template to JSON or YAML");
        var idArg = new Argument<string>("id", "Template id (templateId or GUID)");
        var fileArg = new Argument<FileInfo>("file", "Output file (.json/.yaml/.yml)");
        export.AddArgument(idArg);
        export.AddArgument(fileArg);
        export.SetHandler(async (string id, FileInfo file) =>
        {
            Environment.Exit(await TemplatesCommands.ExportAsync(id, file, default));
        }, idArg, fileArg);

        var import = new Command("import", "Upsert a template from a JSON or YAML file");
        var inFileArg = new Argument<FileInfo>("file", "Input file (.json/.yaml/.yml)");
        import.AddArgument(inFileArg);
        import.SetHandler(async (FileInfo file) =>
        {
            Environment.Exit(await TemplatesCommands.ImportAsync(file, default));
        }, inFileArg);

        cmd.AddCommand(list);
        cmd.AddCommand(export);
        cmd.AddCommand(import);
        return cmd;
    }

    // -------------------------------------------------------------- packs
    /// <summary>
    /// Iter-35 — versioned clinical validation packs. Wraps
    /// <c>/api/validation-packs</c> so admins can import/export the same
    /// on-disk fixture format already used under <c>rulebooks/_tests/</c>.
    /// </summary>
    private static Command BuildPacksCommand()
    {
        var cmd = new Command("packs", "Manage versioned clinical validation packs");

        // list
        var list = new Command("list", "List validation packs (optionally filtered by rulebook)");
        var rbOpt = new Option<string?>("--rulebook", () => null, "Filter by rulebook id (snake_case)");
        list.AddOption(rbOpt);
        list.SetHandler(async (string? rb) =>
            Environment.Exit(await ValidationPacksCommands.ListAsync(rb, default)), rbOpt);

        // import
        var import = new Command("import", "Import a directory of *.json golden cases as a new pack");
        var rbReq = new Option<string>("--rulebook", "Rulebook id (snake_case)") { IsRequired = true };
        var verReq = new Option<string>("--version", "Pack version (semver)") { IsRequired = true };
        var nameOpt = new Option<string?>("--name", () => null, "Human-readable name");
        var dirArg = new Argument<DirectoryInfo>("dir", "Directory of *.json golden case fixtures");
        import.AddOption(rbReq);
        import.AddOption(verReq);
        import.AddOption(nameOpt);
        import.AddArgument(dirArg);
        import.SetHandler(async (string rb, string ver, string? name, DirectoryInfo d) =>
            Environment.Exit(await ValidationPacksCommands.ImportAsync(rb, ver, name, d, default)),
            rbReq, verReq, nameOpt, dirArg);

        // export
        var export = new Command("export", "Export a pack to a JSON file");
        var idArg = new Argument<string>("id", "Pack id (GUID)");
        var fileArg = new Argument<FileInfo>("file", "Output file (.json)");
        export.AddArgument(idArg);
        export.AddArgument(fileArg);
        export.SetHandler(async (string id, FileInfo f) =>
            Environment.Exit(await ValidationPacksCommands.ExportAsync(id, f, default)), idArg, fileArg);

        // run
        var run = new Command("run", "Run a pack against its rulebook and print pass/fail");
        var runIdArg = new Argument<string>("id", "Pack id (GUID)");
        run.AddArgument(runIdArg);
        run.SetHandler(async (string id) =>
            Environment.Exit(await ValidationPacksCommands.RunAsync(id, default)), runIdArg);

        cmd.AddCommand(list);
        cmd.AddCommand(import);
        cmd.AddCommand(export);
        cmd.AddCommand(run);
        return cmd;
    }
    // Helpers delegate to RadioPad.Cli.Commands.CliRuntime so legacy and
    // iter-31 commands share the same config + HTTP client (incl. bearer
    // token persisted by `radiopad login --device-flow`).
    private static CliRuntime.CliConfig LoadConfig() => CliRuntime.LoadConfig();
    private static string LoadServer() => CliRuntime.LoadConfig().Server;
    private static HttpClient NewHttpClient() => CliRuntime.NewHttpClient();

    // --------------------------------------------------------------- ingest
    /// <summary>
    /// PRD INT-001..004 — companion CLI for the inbound ingest webhook.
    /// Useful for upstream-system smoke tests without bouncing through curl.
    /// Reads <c>RADIOPAD_INGEST_BEARER</c> for the tenant secret so the value
    /// never appears on the command line.
    /// </summary>
    private static Command BuildIngestCommand()
    {
        var cmd = new Command("ingest", "Send a synthetic order to /api/ingest/order");
        var accession = new Option<string>("--accession", "Accession number") { IsRequired = true };
        var modality = new Option<string>("--modality", "Modality (CT/MR/XR/...)") { IsRequired = true };
        var bodyPart = new Option<string>("--body-part", () => "", "Body part");
        var indication = new Option<string>("--indication", () => "", "Clinical indication");
        cmd.AddOption(accession);
        cmd.AddOption(modality);
        cmd.AddOption(bodyPart);
        cmd.AddOption(indication);
        cmd.SetHandler(async (string acc, string mod, string bp, string ind) =>
        {
            var bearer = Environment.GetEnvironmentVariable("RADIOPAD_INGEST_BEARER");
            if (string.IsNullOrEmpty(bearer))
            {
                Console.Error.WriteLine("RADIOPAD_INGEST_BEARER environment variable is not set.");
                Environment.Exit(2);
            }
            var cfg = LoadConfig();
            using var http = new HttpClient { BaseAddress = new Uri(cfg.Server) };
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", cfg.Tenant);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            var resp = await http.PostAsJsonAsync("/api/ingest/order", new
            {
                accessionNumber = acc,
                modality = mod,
                bodyPart = bp,
                indication = ind,
            });
            Console.WriteLine($"{(int)resp.StatusCode} {resp.StatusCode}");
            Console.WriteLine(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode) Environment.Exit(1);
        }, accession, modality, bodyPart, indication);

        // PRD INT-002 — `radiopad ingest fhir <file>` posts a FHIR R4
        // ServiceRequest (or Bundle) to /api/ingest/fhir/servicerequest. The
        // bearer secret is only ever read from RADIOPAD_INGEST_BEARER.
        var fhir = new Command("fhir", "POST a FHIR R4 ServiceRequest/Bundle JSON file");
        var fileArg = new Argument<FileInfo>("file", "Path to a FHIR JSON file");
        fhir.AddArgument(fileArg);
        fhir.SetHandler(async (FileInfo file) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                Environment.Exit(2);
            }
            var bearer = Environment.GetEnvironmentVariable("RADIOPAD_INGEST_BEARER");
            if (string.IsNullOrEmpty(bearer))
            {
                Console.Error.WriteLine("RADIOPAD_INGEST_BEARER environment variable is not set.");
                Environment.Exit(2);
            }
            var cfg = LoadConfig();
            using var http = new HttpClient { BaseAddress = new Uri(cfg.Server) };
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", cfg.Tenant);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            var body = await File.ReadAllTextAsync(file.FullName);
            using var content = new StringContent(body, Encoding.UTF8, "application/fhir+json");
            var resp = await http.PostAsync("/api/ingest/fhir/servicerequest", content);
            Console.WriteLine($"{(int)resp.StatusCode} {resp.StatusCode}");
            Console.WriteLine(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode) Environment.Exit(1);
        }, fileArg);
        cmd.AddCommand(fhir);
        return cmd;
    }

    // ----------------------------------------------------------------- dicom
    /// <summary>PRD DCM-001..006 — read the DICOMweb context for a report id.</summary>
    private static Command BuildDicomCommand()
    {
        var cmd = new Command("dicom", "DICOMweb helpers");
        var fetch = new Command("fetch", "Fetch DICOMweb study context for a report");
        var idArg = new Argument<Guid>("report-id", "Report id");
        fetch.AddArgument(idArg);
        fetch.SetHandler(async (Guid id) =>
        {
            using var http = NewHttpClient();
            var resp = await http.GetAsync($"/api/reports/{id}/dicom-context");
            Console.WriteLine($"{(int)resp.StatusCode} {resp.StatusCode}");
            Console.WriteLine(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode) Environment.Exit(1);
        }, idArg);
        cmd.AddCommand(fetch);
        return cmd;
    }

    // ---------------------------------------------------------------- plugin
    /// <summary>
    /// PRD DESK-009 — verify a local plugin or model artifact:
    /// SHA-256 (constant-time) and an optional Ed25519 detached signature
    /// against a public key from <c>RADIOPAD_PLUGIN_PUBKEY</c> (PEM or hex).
    /// </summary>
    private static Command BuildPluginCommand()
    {
        var cmd = new Command("plugin", "Plugin / model artifact tools");
        var verify = new Command("verify", "Verify a plugin file's SHA-256 (and optional Ed25519 signature)");
        var pathArg = new Argument<FileInfo>("path", "Plugin / model file to verify");
        var shaOpt = new Option<string>("--sha256", "Expected lowercase SHA-256 hex digest") { IsRequired = true };
        var sigOpt = new Option<string?>("--signature", "Optional Ed25519 signature (base64 or hex)");
        verify.AddArgument(pathArg);
        verify.AddOption(shaOpt);
        verify.AddOption(sigOpt);
        verify.SetHandler((FileInfo file, string sha, string? sig) =>
        {
            try
            {
                PluginVerifier.Verify(file.FullName, sha, sig);
                Console.WriteLine($"OK  {file.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL  {file.Name}  ({ex.Message})");
                Environment.ExitCode = 1;
            }
        }, pathArg, shaOpt, sigOpt);
        cmd.AddCommand(verify);
        return cmd;
    }

    // ---------------------------------------------------------------- bundle
    /// <summary>
    /// PRD BILL-001..006 — companion CLI for billing exports. Saves the
    /// invoice ZIP returned by <c>GET /api/billing/invoices/export?format=zip</c>
    /// to a local file. The endpoint is the only place that may emit
    /// invoice data; the CLI never builds invoices itself.
    /// </summary>
    private static Command BuildBundleCommand()
    {
        var cmd = new Command("bundle", "Pack tenant data for export / archival");
        var invoices = new Command("export-invoices", "Download a ZIP of invoices for the active tenant");
        var fromOpt = new Option<DateTime?>("--from", "Inclusive start date (yyyy-MM-dd)");
        var toOpt = new Option<DateTime?>("--to", "Inclusive end date (yyyy-MM-dd)");
        var outOpt = new Option<FileInfo>("--out", "Output ZIP path") { IsRequired = true };
        invoices.AddOption(fromOpt);
        invoices.AddOption(toOpt);
        invoices.AddOption(outOpt);
        invoices.SetHandler(async (DateTime? from, DateTime? to, FileInfo outFile) =>
        {
            using var http = NewHttpClient();
            var qs = new List<string> { "format=zip" };
            if (from.HasValue) qs.Add($"from={from.Value:yyyy-MM-dd}");
            if (to.HasValue) qs.Add($"to={to.Value:yyyy-MM-dd}");
            var url = $"/api/billing/invoices/export?{string.Join('&', qs)}";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"{(int)resp.StatusCode} {resp.StatusCode}");
                Console.Error.WriteLine(await resp.Content.ReadAsStringAsync());
                Environment.Exit(1);
            }
            outFile.Directory?.Create();
            await using var fs = outFile.OpenWrite();
            await resp.Content.CopyToAsync(fs);
            Console.WriteLine($"saved {outFile.FullName} ({fs.Length} bytes)");
        }, fromOpt, toOpt, outOpt);
        cmd.AddCommand(invoices);
        return cmd;
    }

    // ----------------------------------------------------------------- pacs
    /// <summary>
    /// Iter-32 DESK-007 / INT-007 — PACS plugin management. The CLI mirrors
    /// the desktop loader so build pipelines and operators can verify
    /// signed manifests without launching the desktop shell.
    /// </summary>
    private static Command BuildPacsCommand()
    {
        var cmd = new Command("pacs", "PACS bridge / signed-plugin tools");
        var plugins = new Command("plugins", "Manage installed PACS plugins");

        // list
        var list = new Command("list", "List installed PACS plugins (verifying each manifest)");
        list.SetHandler(() =>
        {
            var dir = PacsPluginsDir();
            if (dir is null || !Directory.Exists(dir))
            {
                Console.WriteLine("(no plugins directory)");
                return;
            }
            int ok = 0, bad = 0;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var manifestPath = Path.Combine(sub, "manifest.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var (mId, vendor, ver, sha) = ReadManifestSummary(manifestPath);
                    var sig = TryReadSig(sub);
                    PluginVerifier.Verify(manifestPath, sha, sig);
                    Console.WriteLine($"OK    {mId,-24} {vendor,-10} {ver}");
                    ok++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL  {Path.GetFileName(sub),-24}  ({ex.Message})");
                    bad++;
                }
            }
            Console.WriteLine($"\n{ok} verified, {bad} failed");
            if (bad > 0) Environment.ExitCode = 1;
        });

        // verify <path>
        var verify = new Command("verify", "Verify a single plugin manifest file");
        var pathArg = new Argument<FileInfo>("manifest", "Path to manifest.json");
        verify.AddArgument(pathArg);
        verify.SetHandler((FileInfo file) =>
        {
            try
            {
                var (id, _, _, sha) = ReadManifestSummary(file.FullName);
                var sig = TryReadSig(file.DirectoryName!);
                PluginVerifier.Verify(file.FullName, sha, sig);
                Console.WriteLine($"OK  {id}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL  {file.Name}  ({ex.Message})");
                Environment.ExitCode = 1;
            }
        }, pathArg);

        // enable / disable <id>
        var idArg = new Argument<string>("id", "Plugin id (kebab-case)");
        var enable = new Command("enable", "Enable an installed plugin (sets enabled:true)");
        enable.AddArgument(idArg);
        enable.SetHandler((string id) => SetPluginEnabled(id, true), idArg);
        var disable = new Command("disable", "Disable an installed plugin");
        disable.AddArgument(idArg);
        disable.SetHandler((string id) => SetPluginEnabled(id, false), idArg);

        plugins.AddCommand(list);
        plugins.AddCommand(verify);
        plugins.AddCommand(enable);
        plugins.AddCommand(disable);
        cmd.AddCommand(plugins);
        return cmd;
    }

    private static string? PacsPluginsDir()
    {
        var appdata = Environment.GetEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appdata))
            return Path.Combine(appdata, "RadioPad", "plugins");
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home)) return null;
        var mac = Path.Combine(home, "Library", "Application Support", "RadioPad", "plugins");
        var lin = Path.Combine(home, ".local", "share", "RadioPad", "plugins");
        return Directory.Exists(Path.GetDirectoryName(mac)!) ? mac : lin;
    }

    private static (string id, string vendor, string version, string sha256) ReadManifestSummary(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        return (
            r.GetProperty("id").GetString() ?? "",
            r.TryGetProperty("vendor", out var vn) ? vn.GetString() ?? "" : "",
            r.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
            r.GetProperty("sha256").GetString() ?? "");
    }

    private static string? TryReadSig(string dir)
    {
        var p = Path.Combine(dir, "manifest.sig.b64");
        return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
    }

    private static void SetPluginEnabled(string id, bool enabled)
    {
        var dir = PacsPluginsDir();
        if (dir is null) { Console.Error.WriteLine("plugins directory unavailable"); Environment.ExitCode = 1; return; }
        var pluginDir = Path.Combine(dir, id);
        var manifestPath = Path.Combine(pluginDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"plugin '{id}' not installed at {pluginDir}");
            Environment.ExitCode = 1;
            return;
        }
        var json = File.ReadAllText(manifestPath);
        using (var doc = JsonDocument.Parse(json))
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                obj["enabled"] = enabled;
                File.WriteAllText(manifestPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{id} enabled={enabled}");
                return;
            }
        }
        Console.Error.WriteLine("manifest is not a JSON object");
        Environment.ExitCode = 1;
    }
}
