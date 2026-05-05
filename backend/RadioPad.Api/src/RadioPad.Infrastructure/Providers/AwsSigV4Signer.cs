using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace RadioPad.Infrastructure.Providers;

/// <summary>
/// Minimal AWS Signature Version 4 implementation for signing
/// <c>HttpRequestMessage</c> instances against the Bedrock Runtime
/// (<c>bedrock-runtime.{region}.amazonaws.com</c>) service. Scope is
/// intentionally narrow — we only need to sign POST/JSON bodies; multipart
/// uploads, query-string signing, and STS chunked uploads are not supported.
/// Docs: https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
/// </summary>
internal static class AwsSigV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Terminator = "aws4_request";

    /// <summary>
    /// Sign <paramref name="request"/> in place, populating
    /// <c>Host</c>, <c>X-Amz-Date</c>, <c>X-Amz-Content-Sha256</c>, and
    /// <c>Authorization</c> headers. Body content must be set on the
    /// request before calling.
    /// </summary>
    public static async Task SignAsync(
        HttpRequestMessage request,
        string accessKeyId,
        string secretAccessKey,
        string region,
        string service,
        DateTimeOffset? signingTime = null,
        string? sessionToken = null,
        CancellationToken cancellationToken = default)
    {
        if (request.RequestUri is null)
            throw new InvalidOperationException("AwsSigV4Signer: request URI is required.");

        var now = (signingTime ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var bodyBytes = request.Content is null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var payloadHash = HexHash(bodyBytes);

        var host = request.RequestUri.Host;
        request.Headers.Host = host;
        request.Headers.Remove("x-amz-date");
        request.Headers.Add("x-amz-date", amzDate);
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.Add("x-amz-content-sha256", payloadHash);
        if (!string.IsNullOrEmpty(sessionToken))
        {
            request.Headers.Remove("x-amz-security-token");
            request.Headers.Add("x-amz-security-token", sessionToken);
        }

        var canonicalUri = string.IsNullOrEmpty(request.RequestUri.AbsolutePath) ? "/" : request.RequestUri.AbsolutePath;
        var canonicalQuery = request.RequestUri.Query.TrimStart('?'); // empty for our use-case
        var headerPairs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate,
        };
        if (!string.IsNullOrEmpty(sessionToken))
            headerPairs["x-amz-security-token"] = sessionToken;
        if (request.Content?.Headers.ContentType is MediaTypeHeaderValue ct)
            headerPairs["content-type"] = ct.ToString();

        var canonicalHeaders = new StringBuilder();
        foreach (var (k, v) in headerPairs)
        {
            canonicalHeaders.Append(k).Append(':').Append(v.Trim()).Append('\n');
        }
        var signedHeaders = string.Join(';', headerPairs.Keys);

        var canonicalRequest = string.Join('\n', new[]
        {
            request.Method.Method,
            canonicalUri,
            canonicalQuery,
            canonicalHeaders.ToString(),
            signedHeaders,
            payloadHash,
        });

        var credentialScope = $"{dateStamp}/{region}/{service}/{Terminator}";
        var stringToSign = string.Join('\n', new[]
        {
            Algorithm,
            amzDate,
            credentialScope,
            HexHash(Encoding.UTF8.GetBytes(canonicalRequest)),
        });

        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        var kSigning = HmacSha256(kService, Terminator);
        var signature = ToHex(HmacSha256(kSigning, stringToSign));

        var authorization =
            $"{Algorithm} Credential={accessKeyId}/{credentialScope}, " +
            $"SignedHeaders={signedHeaders}, Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    private static string HexHash(byte[] bytes) => ToHex(SHA256.HashData(bytes));

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var h = new HMACSHA256(key);
        return h.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
