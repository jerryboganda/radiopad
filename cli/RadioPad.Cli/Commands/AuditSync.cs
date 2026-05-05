using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RadioPad.Cli.Commands;

/// <summary>
/// CLI-009 — pull tenant audit events to a local NDJSON file for
/// offline / SIEM forwarding. The watermark (last <c>createdAt</c>) is
/// persisted in <c>~/.radiopad/audit-sync.state</c> so subsequent runs
/// only fetch new rows.
/// </summary>
public static class AuditSync
{
    public sealed record SyncState(DateTimeOffset? LastCreatedAt, string? LastEventId);

    public static SyncState ReadState(string path)
    {
        if (!File.Exists(path)) return new SyncState(null, null);
        try
        {
            using var s = File.OpenRead(path);
            var doc = JsonDocument.Parse(s);
            var r = doc.RootElement;
            DateTimeOffset? when = r.TryGetProperty("lastCreatedAt", out var w) && w.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(w.GetString()!, CultureInfo.InvariantCulture)
                : null;
            string? id = r.TryGetProperty("lastEventId", out var i) && i.ValueKind == JsonValueKind.String
                ? i.GetString()
                : null;
            return new SyncState(when, id);
        }
        catch
        {
            return new SyncState(null, null);
        }
    }

    public static void WriteState(string path, SyncState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new
        {
            lastCreatedAt = state.LastCreatedAt,
            lastEventId = state.LastEventId,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }

    /// <summary>
    /// Pure helper — given the raw <c>events</c> array (oldest-first ordered
    /// for our purposes), the current state, and an optional explicit
    /// <c>from</c>/<c>to</c> window, returns the events to append to NDJSON
    /// and the new watermark. Visible for unit tests.
    /// </summary>
    public static (List<JsonElement> ToWrite, SyncState NewState) Filter(
        IReadOnlyList<JsonElement> events,
        SyncState current,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        // The API returns events in descending order; sort ascending by createdAt
        // for stable resumability.
        var sorted = events
            .Select(e => new { El = e, At = e.GetProperty("createdAt").GetDateTimeOffset(), Id = e.GetProperty("id").GetString() ?? "" })
            .OrderBy(x => x.At)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

        var toWrite = new List<JsonElement>();
        var newState = current;
        foreach (var x in sorted)
        {
            if (from is not null && x.At < from) continue;
            if (to is not null && x.At > to) continue;
            if (current.LastCreatedAt is { } lc && x.At < lc) continue;
            if (current.LastCreatedAt is { } lc2 && x.At == lc2 &&
                current.LastEventId is { } li && string.CompareOrdinal(x.Id, li) <= 0) continue;
            toWrite.Add(x.El);
            newState = new SyncState(x.At, x.Id);
        }
        return (toWrite, newState);
    }

    public static async Task<int> RunAsync(FileInfo? outFile, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var cfg = CliRuntime.RequireConfig();
        using var http = CliRuntime.NewHttpClient(cfg);

        var ndjsonPath = outFile?.FullName ?? CliRuntime.DefaultAuditEventsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(ndjsonPath)!);
        var statePath = CliRuntime.AuditSyncStatePath;
        var state = ReadState(statePath);

        int totalWritten = 0;
        int skip = 0;
        const int take = 1000;

        await using var ndjson = new StreamWriter(new FileStream(ndjsonPath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));

        while (true)
        {
            var url = new StringBuilder($"/api/audit?skip={skip}&take={take}");
            if (from is not null) url.Append($"&from={Uri.EscapeDataString(from.Value.ToString("O"))}");
            if (to is not null) url.Append($"&to={Uri.EscapeDataString(to.Value.ToString("O"))}");

            var resp = await http.GetAsync(url.ToString(), ct);
            if (!resp.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"audit sync: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                return CliRuntime.ExitFailure;
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var batch = doc.RootElement.EnumerateArray()
                .Select(e => JsonDocument.Parse(e.GetRawText()).RootElement)
                .ToList();
            if (batch.Count == 0) break;

            var (toWrite, newState) = Filter(batch, state, from, to);
            foreach (var ev in toWrite)
            {
                await ndjson.WriteLineAsync(ev.GetRawText());
                totalWritten++;
            }
            state = newState;

            if (batch.Count < take) break;
            skip += take;
        }
        await ndjson.FlushAsync(ct);
        WriteState(statePath, state);

        Console.WriteLine($"audit sync: wrote {totalWritten} new event(s) to {ndjsonPath}");
        if (state.LastCreatedAt is not null) Console.WriteLine($"watermark: {state.LastCreatedAt:O}");
        return 0;
    }
}
