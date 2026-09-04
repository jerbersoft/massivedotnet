using System.Net;
using System.Text;
using NodaTime;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Replays a scripted sequence of responses, one per request, so a test can drive a handler
/// through a failure and out the other side. <see cref="StubHandler"/> answers every request
/// identically, which cannot express "fails twice, then succeeds".
/// </summary>
/// <remarks>
/// The last scripted response repeats once the script is exhausted, so a test asserting an
/// attempt cap does not have to script one entry per attempt.
/// </remarks>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly IReadOnlyList<(HttpStatusCode Status, Duration? RetryAfter)> _script;
    private readonly List<Uri?> _requestUris = [];
    private readonly Lock _gate = new();

    public ScriptedHandler(params HttpStatusCode[] statuses)
        : this([.. statuses.Select(status => (status, (Duration?)null))])
    {
    }

    public ScriptedHandler(IReadOnlyList<(HttpStatusCode Status, Duration? RetryAfter)> script)
    {
        ArgumentOutOfRangeException.ThrowIfZero(script.Count);
        _script = script;
    }

    /// <summary>Every request URI seen, in order — the ordering pin reads this (D30).</summary>
    public IReadOnlyList<Uri?> RequestUris
    {
        get
        {
            lock (_gate)
            {
                return [.. _requestUris];
            }
        }
    }

    public int RequestCount
    {
        get
        {
            lock (_gate)
            {
                return _requestUris.Count;
            }
        }
    }

    /// <summary>A body every endpoint under test can deserialize, so success is reachable.</summary>
    public string Body { get; init; } = """{"status":"OK","results":[]}""";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int index;
        lock (_gate)
        {
            index = _requestUris.Count;
            _requestUris.Add(request.RequestUri);
        }

        (HttpStatusCode status, Duration? retryAfter) = _script[Math.Min(index, _script.Count - 1)];

        HttpResponseMessage response = new(status)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        };

        if (retryAfter is { } delay)
        {
            // Boundary crossing (produce): converted inline, so the BCL type is never named.
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(delay.ToTimeSpan());
        }

        return Task.FromResult(response);
    }
}
