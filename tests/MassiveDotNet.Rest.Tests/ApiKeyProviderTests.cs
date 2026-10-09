using System.Net;
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// D45: <see cref="MassiveClientOptions.ApiKeyProvider"/> supplies the key on every request, so one
/// long-lived client follows a key that changes while it runs. Asserted through the pipeline
/// <see cref="MassiveHttpTransport.CreateHandlerPipeline"/> builds, which is what the owning
/// transport sends through.
/// </summary>
public sealed class ApiKeyProviderTests
{
    private static readonly string?[] BothKeys = ["key-one", "key-two"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A key a test can change while requests are in flight.</summary>
    private sealed class RotatingKey(string initial)
    {
        private string _current = initial;

        public string Current
        {
            get => Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, value);
        }
    }

    /// <summary>
    /// Records the key each request carried, under either scheme, at the moment it was sent, and
    /// answers from a script whose last status repeats.
    /// </summary>
    private sealed class KeyRecordingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string?> _bearerKeys = [];
        private readonly List<string?> _queries = [];

        public IReadOnlyList<string?> BearerKeys
        {
            get
            {
                lock (_gate)
                {
                    return [.. _bearerKeys];
                }
            }
        }

        public IReadOnlyList<string?> Queries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _queries];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int index;
            lock (_gate)
            {
                index = _bearerKeys.Count;
                _bearerKeys.Add(request.Headers.Authorization?.Parameter);
                _queries.Add(request.RequestUri?.Query);
            }

            HttpStatusCode status = statuses.Length == 0
                ? HttpStatusCode.OK
                : statuses[Math.Min(index, statuses.Length - 1)];

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"status":"OK","results":[]}"""),
                RequestMessage = request,
            });
        }
    }

    private static async Task WithClientAsync(
        MassiveClientOptions options,
        HttpMessageHandler inner,
        Func<MassiveRestClient, Task> act)
    {
        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        await act(client);
    }

    [Fact]
    public async Task TheProviderIsReadOnEveryRequestOfOneLongLivedClient()
    {
        RotatingKey key = new("key-one");
        KeyRecordingHandler inner = new();
        MassiveClientOptions options = new() { ApiKeyProvider = () => key.Current };

        await WithClientAsync(options, inner, async client =>
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
            key.Current = "key-two";
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        });

        Assert.Equal(["key-one", "key-two"], inner.BearerKeys);
    }

    [Fact]
    public async Task TheProviderWinsOverTheStaticKey()
    {
        KeyRecordingHandler inner = new();
        MassiveClientOptions options = new()
        {
            ApiKey = "static-key",
            ApiKeyProvider = () => "provided-key",
        };

        await WithClientAsync(options, inner, async client =>
            await client.Reference.ListTickersAsync(cancellationToken: Ct));

        Assert.Equal(["provided-key"], inner.BearerKeys);
    }

    [Fact]
    public void AProviderAloneSatisfiesValidation()
    {
        MassiveClientOptions options = new() { ApiKeyProvider = () => "provided-key" };

        Exception? error = Record.Exception(options.Validate);

        Assert.Null(error);
        Assert.Null(options.ApiKey);
    }

    [Fact]
    public void NeitherAKeyNorAProviderFailsValidationNamingBoth()
    {
        MassiveClientOptions options = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains($"{nameof(MassiveClientOptions)}.{nameof(MassiveClientOptions.ApiKey)} ", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MassiveClientOptions.ApiKeyProvider), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyProvidedKeyFailsTheRequestBeforeAnythingIsSent(string? provided)
    {
        // Rule 11: the message names the option, never a value.
        KeyRecordingHandler inner = new();
        MassiveClientOptions options = new() { ApiKeyProvider = () => provided! };

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => httpClient.GetAsync("v3/reference/tickers", Ct));

        Assert.Contains(nameof(MassiveClientOptions.ApiKeyProvider), error.Message, StringComparison.Ordinal);
        Assert.Empty(inner.BearerKeys);
    }

    [Fact]
    public async Task AProviderThatThrowsFailsTheRequestUnchangedAndSendsNothing()
    {
        // A consumer whose key store has not loaded yet sees its own exception, not one the SDK invents.
        KeyRecordingHandler inner = new();
        InvalidOperationException thrown = new("the key store has not loaded");
        MassiveClientOptions options = new() { ApiKeyProvider = () => throw thrown };

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => httpClient.GetAsync("v3/reference/tickers", Ct));

        Assert.Same(thrown, error);
        Assert.Empty(inner.BearerKeys);
    }

    [Fact]
    public async Task ARetriedRequestCarriesTheKeyReadOnceAtItsStart()
    {
        // Authentication is outermost (D30), so the provider runs once per logical request and every
        // attempt beneath it carries that one key -- appended once, under the scheme that rewrites
        // the URI. A provider that answers differently on every call proves it was not re-read.
        int calls = 0;
        KeyRecordingHandler inner = new(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        MassiveClientOptions options = new()
        {
            ApiKeyProvider = () => $"key-{Interlocked.Increment(ref calls)}",
            AuthenticationScheme = MassiveAuthenticationScheme.QueryString,
            Retry = new MassiveRetryOptions
            {
                MaxAttempts = 2,
                InitialBackoff = Duration.FromMilliseconds(1),
                MaxBackoff = Duration.FromMilliseconds(2),
            },
        };

        await WithClientAsync(options, inner, async client =>
            await client.Reference.ListTickersAsync(cancellationToken: Ct));

        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(2, inner.Queries.Count);
        Assert.All(inner.Queries, query =>
        {
            Assert.Equal(1, (query ?? string.Empty).Split("apiKey=", StringSplitOptions.None).Length - 1);
            Assert.Contains("apiKey=key-1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ConcurrentRequestsEachCarryOneWholeKeyWhileItChanges()
    {
        RotatingKey key = new("key-one");
        KeyRecordingHandler inner = new();
        MassiveClientOptions options = new() { ApiKeyProvider = () => key.Current };

        await WithClientAsync(options, inner, async client =>
        {
            Task[] requests =
            [
                .. Enumerable.Range(0, 32).Select(async i =>
                {
                    if (i == 16)
                    {
                        key.Current = "key-two";
                    }

                    await client.Reference.ListTickersAsync(cancellationToken: Ct);
                }),
            ];

            await Task.WhenAll(requests);
        });

        Assert.Equal(32, inner.BearerKeys.Count);
        Assert.All(inner.BearerKeys, sent => Assert.Contains(sent, BothKeys));
        Assert.Contains("key-two", inner.BearerKeys);
    }
}
