using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Auth;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// End-to-end tests for the SSE stream (PR-B1) through the full middleware + controller
/// pipeline. SSE IS testable via WebApplicationFactory: send with
/// <see cref="HttpCompletionOption.ResponseHeadersRead"/> and read the content stream
/// line-by-line under a test deadline. Keep-alive is squeezed to 1s (a derived factory)
/// so assertions run in a few seconds.
/// </summary>
public class EventsStreamTests : IClassFixture<EventsStreamTests.SseFactory>
{
    private readonly SseFactory _factory;
    public EventsStreamTests(SseFactory factory) => _factory = factory;

    /// <summary>Base factory with a 1-second SSE keep-alive so the tests don't wait 15s.</summary>
    public sealed class SseFactory : RadioPadAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AiJobs:SseKeepAliveSeconds", "1");
        }
    }

    private const string StreamUrl = "/api/events/stream";

    private static async Task<(HttpResponseMessage resp, StreamReader reader)> OpenStreamAsync(
        HttpClient client, string url, CancellationToken ct)
    {
        var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return (resp, new StreamReader(stream));
    }

    /// <summary>Reads lines until one matches, or returns null on timeout / stream end.</summary>
    private static async Task<string?> ReadLineUntilAsync(StreamReader reader, Func<string, bool> match, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null) return null;
                if (match(line)) return line;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
    }

    private async Task<string> MintSessionBearerAsync()
    {
        var token = RadioPadBearerTokens.Mint(_factory.SeedTenant.Slug, _factory.SeedUser.Email, _factory.SeedUser.SessionEpoch);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var dbUser = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(
            db, dbUser, token, "test", RadioPadBearerTokens.ExpiresAt(DateTimeOffset.UtcNow), CancellationToken.None);
        return token;
    }

    [Fact]
    public async Task Stream_RequiresAuth_401WithoutIdentity()
    {
        using var client = _factory.CreateClient(); // no dev headers, no bearer
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var resp = await client.GetAsync(StreamUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Stream_EmitsKeepAliveComments()
    {
        using var client = _factory.CreateTenantClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (resp, reader) = await OpenStreamAsync(client, StreamUrl, cts.Token);
        using (resp)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var line = await ReadLineUntilAsync(reader, l => l.StartsWith(":"), TimeSpan.FromSeconds(3));
            Assert.NotNull(line); // ": keep-alive"
        }
    }

    [Fact]
    public async Task Stream_DeliversTerminalJobEventToOwner()
    {
        using var client = _factory.CreateTenantClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (resp, reader) = await OpenStreamAsync(client, StreamUrl, cts.Token);
        using (resp)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            // Headers received → the controller has already created its subscription.
            await Task.Delay(300, cts.Token);

            var bus = _factory.Services.GetRequiredService<IAiJobEventBus>();
            var job = new AiJob
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = _factory.SeedUser.Id,
                ReportId = Guid.NewGuid(),
                Kind = "ai",
                Mode = "impression",
                Status = "ok",
                CompletedAt = DateTimeOffset.UtcNow,
            };
            bus.PublishTerminal(job);

            var eventLine = await ReadLineUntilAsync(reader, l => l.StartsWith("event: job"), TimeSpan.FromSeconds(5));
            Assert.NotNull(eventLine);

            var dataLine = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(dataLine);
            Assert.StartsWith("data: ", dataLine);
            Assert.Contains(job.Id.ToString(), dataLine); // camelCase jobId payload
            Assert.Contains("\"status\":\"ok\"", dataLine);
        }
    }

    [Fact]
    public async Task Stream_FiltersOtherUsersEvents()
    {
        using var client = _factory.CreateTenantClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var (resp, reader) = await OpenStreamAsync(client, StreamUrl, cts.Token);
        using (resp)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            await Task.Delay(300, cts.Token);

            var bus = _factory.Services.GetRequiredService<IAiJobEventBus>();
            // Same tenant, DIFFERENT user → the owner's stream must not see it.
            bus.PublishTerminal(new AiJob
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = Guid.NewGuid(),
                ReportId = Guid.NewGuid(),
                Kind = "ai",
                Mode = "impression",
                Status = "ok",
                CompletedAt = DateTimeOffset.UtcNow,
            });

            var leaked = await ReadLineUntilAsync(reader, l => l.StartsWith("event: job"), TimeSpan.FromSeconds(2));
            Assert.Null(leaked); // only keep-alives arrived
        }
    }

    [Fact]
    public async Task Stream_AccessTokenQueryParamAuthenticates()
    {
        var token = await MintSessionBearerAsync();
        using var client = _factory.CreateClient(); // NO headers — a webview EventSource
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var url = $"{StreamUrl}?access_token={Uri.EscapeDataString(token)}";
        var (resp, reader) = await OpenStreamAsync(client, url, cts.Token);
        using (resp)
        using (reader)
        {
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var line = await ReadLineUntilAsync(reader, l => l.StartsWith(":"), TimeSpan.FromSeconds(3));
            Assert.NotNull(line);
        }
    }

    [Fact]
    public async Task Stream_ClosesOnClientAbort()
    {
        var bus = (AiJobEventBus)_factory.Services.GetRequiredService<IAiJobEventBus>();
        // Start from a clean slate — earlier tests' streams unsubscribe on dispose.
        await WaitForAsync(() => bus.SubscriberCount == 0, TimeSpan.FromSeconds(10));

        using var client = _factory.CreateTenantClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var (resp, reader) = await OpenStreamAsync(client, StreamUrl, cts.Token);
        // Confirm the connection is live (subscription created) before aborting.
        var line = await ReadLineUntilAsync(reader, l => l.StartsWith(":"), TimeSpan.FromSeconds(3));
        Assert.NotNull(line);
        Assert.True(bus.SubscriberCount >= 1);

        // Abort the client side → the server observes RequestAborted and unsubscribes.
        reader.Dispose();
        resp.Dispose();

        await WaitForAsync(() => bus.SubscriberCount == 0, TimeSpan.FromSeconds(10));
        Assert.Equal(0, bus.SubscriberCount);
    }
}
