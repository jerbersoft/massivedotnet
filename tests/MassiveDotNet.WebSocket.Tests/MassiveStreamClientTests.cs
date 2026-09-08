using System.Net.WebSockets;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Covers <see cref="MassiveStreamClient"/>'s own lifecycle -- H3(a) and H3(b) from the Task 12
/// pre-flight, reachable precisely because <c>AddMassiveStream</c> registers this type as a
/// singleton (D28) and its own XML doc says "long-lived and shared": more than one caller can
/// legitimately hold a reference and call into it concurrently.
/// </summary>
/// <remarks>
/// Shares the "StreamConcurrencyStress" collection with <c>StockStreamTests</c> (Task 12 report):
/// this class's own concurrency test blocks dozens of raw OS threads on thread-pool-served
/// continuations, and xUnit runs different classes' collections in parallel by default -- running
/// at the same time as StockStreamTests' own heavy test starved the pool badly enough to produce a
/// spurious multi-second timeout, observed directly. The shared collection makes the two run
/// sequentially relative to each other without slowing down the rest of the suite, which still
/// runs in parallel with both.
/// </remarks>
[Collection("StreamConcurrencyStress")]
public class MassiveStreamClientTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static FakeWebSocket ConnectedSocket()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);
        return socket;
    }

    // H3(b): DisposeAsync had no disposal guard, so a stream opened after disposal -- socket open,
    // read loop running -- was added to a list DisposeAsync had already iterated and cleared.
    // Nobody would ever dispose it again: a leaked connection with no error anywhere. Deterministic
    // (sequential, no threads): dispose first, then attempt to connect, and assert both that the
    // attempt is refused AND that it never got as far as opening the second socket -- "refused"
    // rather than "opened, then abandoned".
    [Fact]
    public async Task ConnectingAfterDisposalIsRefusedRatherThanLeakingTheStream()
    {
        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k" });

        FakeWebSocket first = ConnectedSocket();
        await client.ConnectStocksAsync(() => first, TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        FakeWebSocket second = ConnectedSocket();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.ConnectStocksAsync(() => second, TestContext.Current.CancellationToken));

        Assert.Equal(0, second.ConnectCount);
    }

    // Idempotency companion to the guard above: disposing twice must not throw, matching every
    // other IAsyncDisposable in this SDK (MassiveStreamConnection.DisposeAsync, ClientWebSocketAdapter).
    [Fact]
    public async Task DisposingTwiceIsHarmless()
    {
        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k" });

        FakeWebSocket socket = ConnectedSocket();
        await client.ConnectStocksAsync(() => socket, TestContext.Current.CancellationToken);

        await client.DisposeAsync();
        await client.DisposeAsync();

        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    // H3(a): _streams was a plain, unsynchronized List<IAsyncDisposable> that ConnectStocksAsync
    // appended to. List<T>.Add is not thread-safe -- concurrent calls can throw outright or lose an
    // entry silently, either way orphaning whatever connection that call just opened, since nothing
    // else will ever dispose it. 64 threads released together off a Barrier, each opening its own
    // stream over its own FakeWebSocket, is what forces genuine contention on the unsynchronized
    // list (mirrors DispatchTests.RegisteringManySinksConcurrentlyLosesNone's reasoning for the
    // same class of defect on MassiveStreamConnection's own sink table). The assertion that matters
    // is the one after every thread has joined: every single socket this test opened must end up
    // Closed once the client is disposed -- if even one is missing from the client's own
    // bookkeeping, it stays open forever with nothing left alive to close it.
    [Fact]
    public async Task ConnectingManyStreamsConcurrentlyRegistersEveryOneForDisposal()
    {
        const int ParticipantCount = 32;

        // Pre-warms the pool so this test's own concurrency does not starve continuations an
        // unrelated, concurrently-running test in the same process (xUnit parallelizes across test
        // classes) needs to make progress -- observed directly as a spurious multi-second stall on
        // StockStreamTests before this fix (see the Task 12 report).
        ThreadPool.GetMinThreads(out int minWorkerThreads, out int minCompletionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(minWorkerThreads, ParticipantCount + 8), minCompletionPortThreads);

        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k" });
        FakeWebSocket[] sockets = new FakeWebSocket[ParticipantCount];

        for (int i = 0; i < ParticipantCount; i++)
        {
            sockets[i] = ConnectedSocket();
        }

        using Barrier startGate = new(ParticipantCount);
        Thread[] threads = new Thread[ParticipantCount];
        Exception?[] failures = new Exception?[ParticipantCount];

        for (int i = 0; i < ParticipantCount; i++)
        {
            int index = i;

            threads[i] = new Thread(() =>
            {
                try
                {
                    startGate.SignalAndWait();
                    client.ConnectStocksAsync(() => sockets[index], TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception error)
                {
                    failures[index] = error;
                }
            });
        }

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.All(failures, failure => Assert.Null(failure));

        await client.DisposeAsync();

        Assert.All(sockets, socket => Assert.Equal(WebSocketState.Closed, socket.State));
    }
}
