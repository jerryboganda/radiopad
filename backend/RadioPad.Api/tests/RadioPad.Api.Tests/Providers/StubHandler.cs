using System.Net;
using System.Text;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// Tiny stub <see cref="HttpMessageHandler"/> + <see cref="IHttpClientFactory"/>
/// used by the production AI provider adapter tests so we never touch the
/// network. Each adapter test wires a fresh stub with canned JSON.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Responder { get; set; }
    public List<HttpRequestMessage> Captured { get; } = new();
    public List<string> CapturedBodies { get; } = new();

    public static StubHandler Json(HttpStatusCode status, string json)
        => new()
        {
            Responder = (_, _) => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }),
        };

    public static StubHandler Sequence(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return new()
        {
            Responder = (_, _) => Task.FromResult(queue.Count > 0
                ? queue.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("exhausted") }),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Captured.Add(request);
        if (request.Content is not null)
        {
            CapturedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }
        else
        {
            CapturedBodies.Add("");
        }
        if (Responder is null) throw new InvalidOperationException("StubHandler.Responder is not set.");
        return await Responder(request, cancellationToken);
    }
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
