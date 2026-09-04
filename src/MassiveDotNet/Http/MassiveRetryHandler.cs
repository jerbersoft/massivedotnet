using System.Net;
using NodaTime;

namespace MassiveDotNet.Http;

/// <summary>
/// Retries a failed request a bounded number of times, with exponential backoff and jitter.
/// </summary>
/// <remarks>
/// <para>
/// Only HTTP 429 and 5xx are retried, along with the transport-level failures that describe a
/// connection rather than an answer. Every other 4xx describes a request that will fail
/// identically however often it is sent.
/// </para>
/// <para>
/// <strong>This handler must sit inside <see cref="MassiveAuthenticationHandler"/>.</strong> Under
/// <see cref="MassiveAuthenticationScheme.QueryString"/> the authentication handler rewrites the
/// request URI, so a retry placed outside it re-authenticates each attempt and appends the key
/// again — producing <c>?apiKey=k&amp;apiKey=k&amp;apiKey=k</c>. The request still succeeds, so
/// the only symptom is the key reaching access logs once per attempt, which is what constitution
/// rule 11 exists to prevent. <see cref="MassiveHttpTransport.CreateHandlerPipeline"/> composes
/// the order correctly; assemble it by hand only with that ordering in mind (D30).
/// </para>
/// </remarks>
public sealed class MassiveRetryHandler : DelegatingHandler
{
    private readonly MassiveRetryOptions _options;

    /// <summary>Initializes a new instance of the <see cref="MassiveRetryHandler"/> class.</summary>
    /// <param name="options">The retry policy to apply.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> holds a value it cannot act on.</exception>
    public MassiveRetryHandler(MassiveRetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. The SDK issues no synchronous requests.</exception>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            $"{nameof(MassiveRetryHandler)} does not support synchronous sends: waiting out a backoff " +
            "would have to block the calling thread. Use the asynchronous API.");

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Re-sending a request whose body has already been read is not guaranteed to work, and
        // failing on the second attempt would be harder to diagnose than not retrying at all.
        // Every SDK route is a GET, so this guard costs nothing and keeps that assumption honest.
        if (request.Content is not null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (attempt >= _options.MaxAttempts || !IsRetryable(response.StatusCode))
            {
                return response;
            }

            Duration backoff = ComputeBackoff(attempt);

            // Boundary crossing (consume): the pattern match leaves the temporary implicitly
            // typed, so the BCL temporal type is never named (rule 12).
            if (response.Headers.RetryAfter?.Delta is { } delta)
            {
                Duration hint = Duration.FromTimeSpan(delta);

                // A server may name a period far longer than the caller agreed to wait. Holding
                // their task for it is worse than reporting the throttle, so the response is
                // returned unretried and surfaces with its Retry-After intact (D30).
                if (hint > _options.MaxBackoff)
                {
                    return response;
                }

                backoff = hint;
            }

            // The failed response holds the connection until it is disposed, and nothing above
            // will see it now that a further attempt is going out.
            response.Dispose();

            if (backoff > Duration.Zero)
            {
                // Boundary crossing (produce): converted inline at the BCL call site.
                await Task.Delay(backoff.ToTimeSpan(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private Duration ComputeBackoff(int attempt)
    {
        double scale = Math.Pow(_options.BackoffMultiplier, attempt - 1);
        double ticks = _options.InitialBackoff.TotalTicks * scale;

        Duration backoff = double.IsFinite(ticks) && ticks < _options.MaxBackoff.TotalTicks
            ? Duration.FromTicks((long)ticks)
            : _options.MaxBackoff;

        if (!_options.UseJitter || backoff <= Duration.Zero)
        {
            return backoff;
        }

        // Equal jitter: keep the lower half as a floor and randomize the upper half. Full jitter
        // would allow a near-zero wait, which retries hardest exactly when the server is least
        // able to answer; no jitter at all has every throttled client retry in lockstep.
        double half = backoff.TotalTicks / 2.0;
        return Duration.FromTicks((long)(half + (Random.Shared.NextDouble() * half)));
    }
}
