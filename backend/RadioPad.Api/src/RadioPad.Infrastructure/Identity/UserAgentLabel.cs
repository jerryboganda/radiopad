using System.Text.RegularExpressions;

namespace RadioPad.Infrastructure.Identity;

/// <summary>
/// Best-effort, dependency-free User-Agent parsing for the "Active sessions" display
/// (AUTH-009). Deliberately coarse and never guesses past what the string actually
/// says — e.g. Windows NT 10.0 covers both Windows 10 and 11, so it is labelled
/// "Windows" rather than picking one. This is a convenience label for the account
/// owner, not a fingerprinting or bot-detection signal.
/// </summary>
public static class UserAgentLabel
{
    public static (string? Category, string? Detail) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return (null, null);
        var ua = userAgent;

        string category =
            ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) ? "iOS device" :
            ua.Contains("Android", StringComparison.Ordinal) ? "Android device" :
            ua.Contains("Macintosh", StringComparison.Ordinal) || ua.Contains("Mac OS X", StringComparison.Ordinal) ? "Mac" :
            ua.Contains("Windows", StringComparison.Ordinal) ? "Windows Desktop" :
            ua.Contains("Linux", StringComparison.Ordinal) ? "Linux Desktop" :
            "Unknown device";

        var browser = ExtractBrowser(ua);
        var os = ExtractOs(ua);
        string? detail = (browser, os) switch
        {
            (not null, not null) => $"{browser} on {os}",
            (not null, null) => browser,
            (null, not null) => os,
            _ => null,
        };

        return (category, detail);
    }

    private static string? ExtractBrowser(string ua)
    {
        // Order matters: Edge/Opera also contain "Chrome/", Chrome also contains "Safari".
        var m = Regex.Match(ua, @"Edg/(?<v>[\d.]+)");
        if (m.Success) return $"Edge {MajorVersion(m.Groups["v"].Value)}";
        m = Regex.Match(ua, @"OPR/(?<v>[\d.]+)");
        if (m.Success) return $"Opera {MajorVersion(m.Groups["v"].Value)}";
        m = Regex.Match(ua, @"Chrome/(?<v>[\d.]+)");
        if (m.Success) return $"Chrome {MajorVersion(m.Groups["v"].Value)}";
        m = Regex.Match(ua, @"Firefox/(?<v>[\d.]+)");
        if (m.Success) return $"Firefox {MajorVersion(m.Groups["v"].Value)}";
        if (ua.Contains("Safari", StringComparison.Ordinal))
        {
            m = Regex.Match(ua, @"Version/(?<v>[\d.]+)");
            return m.Success ? $"Safari {MajorVersion(m.Groups["v"].Value)}" : "Safari";
        }
        return null;
    }

    private static string? ExtractOs(string ua)
    {
        var m = Regex.Match(ua, @"Windows NT (?<v>[\d.]+)");
        if (m.Success) return WindowsName(m.Groups["v"].Value);
        m = Regex.Match(ua, @"Mac OS X (?<v>[\d_]+)");
        if (m.Success) return $"macOS {m.Groups["v"].Value.Replace('_', '.')}";
        m = Regex.Match(ua, @"Android (?<v>[\d.]+)");
        if (m.Success) return $"Android {m.Groups["v"].Value}";
        m = Regex.Match(ua, @"OS (?<v>[\d_]+) like Mac OS X");
        if (m.Success) return $"iOS {m.Groups["v"].Value.Replace('_', '.')}";
        if (ua.Contains("Linux", StringComparison.Ordinal)) return "Linux";
        return null;
    }

    private static string MajorVersion(string v) => v.Split('.')[0];

    // Windows NT 10.0 is shared by Windows 10 and 11 — no reliable signal in the
    // UA string distinguishes them, so it stays generic rather than guessing.
    private static string WindowsName(string ntVersion) => ntVersion switch
    {
        "6.3" => "Windows 8.1",
        "6.2" => "Windows 8",
        "6.1" => "Windows 7",
        _ => "Windows",
    };
}
