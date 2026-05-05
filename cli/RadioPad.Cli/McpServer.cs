using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RadioPad.Cli;

/// <summary>
/// PRD §17.4 — Read-only Model Context Protocol (MCP) server. Exposes
/// four tools so external assistants (e.g. Claude Desktop, Cursor) can
/// query a RadioPad workspace without write permissions:
///   <list type="bullet">
///   <item><c>list_rulebooks</c> — returns the tenant's rulebooks (status + version).</item>
///   <item><c>get_report_validation</c> — returns the validation findings for one report.</item>
///   <item><c>get_audit_summary</c> — returns counts grouped by `AuditAction` for a date window.</item>
///   <item><c>search_templates</c> — searches report templates by modality / body part.</item>
///   </list>
///
/// Wire format is JSON-RPC 2.0 over stdio per the MCP spec; auth is the same
/// `(tenant, user)` headers the CLI already uses (loaded from
/// <c>~/.radiopad/config.json</c>). The server cannot mutate state — every
/// tool maps to a GET-only RadioPad endpoint.
/// </summary>
public static class McpServer
{
    public static async Task<int> RunAsync()
    {
        var http = NewHttpClient();
        var stdin = Console.In;
        var stdout = Console.Out;
        string? line;
        while ((line = await stdin.ReadLineAsync()) is not null)
        {
            JsonNode? req;
            try { req = JsonNode.Parse(line); }
            catch { continue; }
            if (req is null) continue;
            var id = req["id"];
            var method = req["method"]?.GetValue<string>();

            JsonObject response;
            try
            {
                response = method switch
                {
                    "initialize" => Init(id),
                    "tools/list" => ToolsList(id),
                    "tools/call" => await ToolsCall(http, id, req["params"]),
                    _ => Error(id, -32601, "method not found"),
                };
            }
            catch (Exception ex)
            {
                response = Error(id, -32000, ex.Message);
            }

            await stdout.WriteLineAsync(response.ToJsonString());
            await stdout.FlushAsync();
        }
        return 0;
    }

    private static JsonObject Init(JsonNode? id) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "radiopad-mcp", ["version"] = "0.1.0" },
        },
    };

    private static JsonObject ToolsList(JsonNode? id) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = new JsonObject
        {
            ["tools"] = new JsonArray
            {
                Tool("list_rulebooks", "List rulebooks visible to the active tenant.",
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
                Tool("get_report_validation", "Return validation findings for a report.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["reportId"] = new JsonObject { ["type"] = "string" },
                        },
                        ["required"] = new JsonArray { "reportId" },
                    }),
                Tool("get_audit_summary", "Counts of audit events grouped by action.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["from"] = new JsonObject { ["type"] = "string" },
                            ["to"] = new JsonObject { ["type"] = "string" },
                        },
                    }),
                Tool("search_templates", "Search report templates by modality or body part.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["modality"] = new JsonObject { ["type"] = "string" },
                            ["bodyPart"] = new JsonObject { ["type"] = "string" },
                        },
                    }),
            },
        },
    };

    private static JsonObject Tool(string name, string description, JsonObject inputSchema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = inputSchema,
    };

    private static async Task<JsonObject> ToolsCall(HttpClient http, JsonNode? id, JsonNode? p)
    {
        var name = p?["name"]?.GetValue<string>();
        var args = p?["arguments"] as JsonObject ?? new JsonObject();

        // Iter-32 MCP-005/007 — consult the registry. Only Approved tools
        // (or built-in tools with no registry row at all) may run. We also
        // refuse any tool whose registered scope string contains a
        // dangerous prefix unless the tenant has flipped the override.
        var registryDecision = await ConsultRegistryAsync(http, name);
        if (!registryDecision.Allowed)
            throw new InvalidOperationException($"mcp_blocked: {registryDecision.Reason}");

        string body = name switch
        {
            "list_rulebooks" => await GetText(http, "/api/rulebooks"),
            "get_report_validation" => await GetText(http, $"/api/reports/{args["reportId"]?.GetValue<string>()}/validate"),
            "get_audit_summary" => await GetText(http,
                "/api/usage/analytics" +
                (args["from"] is { } f ? $"?from={f.GetValue<string>()}&to={args["to"]?.GetValue<string>() ?? ""}" : "")),
            "search_templates" => await GetText(http,
                "/api/templates" +
                (args["modality"] is { } m || args["bodyPart"] is { } b
                    ? $"?modality={args["modality"]?.GetValue<string>() ?? ""}&bodyPart={args["bodyPart"]?.GetValue<string>() ?? ""}"
                    : "")),
            _ => throw new InvalidOperationException($"Unknown tool: {name}"),
        };
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = body },
                },
            },
        };
    }

    private record RegistryDecision(bool Allowed, string? Reason);

    private static readonly string[] DangerousScopePrefixes = { "shell:", "fs:", "net:" };

    private static async Task<RegistryDecision> ConsultRegistryAsync(HttpClient http, string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return new RegistryDecision(false, "missing_tool_name");
        try
        {
            var resp = await http.GetAsync("/api/mcp/tools");
            if (!resp.IsSuccessStatusCode) return new RegistryDecision(true, null); // registry unreachable → fall back to built-ins
            var rows = await resp.Content.ReadFromJsonAsync<List<JsonElement>>() ?? new();
            JsonElement? match = null;
            foreach (var row in rows)
            {
                if (row.TryGetProperty("name", out var n) && string.Equals(n.GetString(), toolName, StringComparison.Ordinal))
                {
                    match = row; break;
                }
            }
            if (match is null) return new RegistryDecision(true, null); // built-in, no row
            var status = match.Value.TryGetProperty("status", out var st) ? st.GetInt32() : 0;
            if (status != 1) return new RegistryDecision(false, status == 2 ? "blocked" : "not_approved");
            var scope = match.Value.TryGetProperty("scopeString", out var sc) ? sc.GetString() ?? "" : "";
            if (HasDangerousScope(scope))
            {
                var allowEnv = string.Equals(Environment.GetEnvironmentVariable("RADIOPAD_MCP_ALLOW_DANGEROUS"), "1", StringComparison.Ordinal);
                if (!allowEnv) return new RegistryDecision(false, "mcp_scope_blocked");
            }
            return new RegistryDecision(true, null);
        }
        catch
        {
            // Network failure: fall back to default-allow for built-ins so an
            // offline radiologist can still use the safe read-only tools.
            return new RegistryDecision(true, null);
        }
    }

    private static bool HasDangerousScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return false;
        foreach (var token in scope.Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var p in DangerousScopePrefixes)
                if (token.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task<string> GetText(HttpClient http, string url)
    {
        var resp = await http.GetAsync(url);
        return await resp.Content.ReadAsStringAsync();
    }

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static HttpClient NewHttpClient()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".radiopad");
        var path = Path.Combine(dir, "config.json");
        var server = "http://127.0.0.1:7457"; var tenant = "dev"; var user = "radiologist@radiopad.local";
        if (File.Exists(path))
        {
            var cfg = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            server = cfg.GetProperty("server").GetString() ?? server;
            tenant = cfg.GetProperty("tenant").GetString() ?? tenant;
            user = cfg.GetProperty("user").GetString() ?? user;
        }
        var http = new HttpClient { BaseAddress = new Uri(server) };
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", tenant);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", user);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }
}
