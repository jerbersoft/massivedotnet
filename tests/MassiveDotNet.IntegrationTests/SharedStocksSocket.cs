using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Serialises every live test that opens a stocks WebSocket, because the account has fewer
/// simultaneous connections than xUnit has threads.
/// </summary>
/// <remarks>
/// <para>
/// xUnit runs each test class as its own collection, in parallel with the others. Two classes here
/// open a stocks socket, so they ran concurrently against a key whose plan permits one — and the
/// server answered the second with <c>"Maximum number of websocket connections exceeded. "</c>
/// instead of the refusal the test was pinning. Observed 2026-09-09: the full live tier failed on
/// <c>TheImbalanceTopicIsStillNotAuthorizedOnThisKey</c> while that same test passed alone, and the
/// two stream classes reproduced it on their own.
/// </para>
/// <para>
/// Worth being precise about what was wrong, because nothing in the SDK was: it reported the
/// server's message verbatim, which is exactly D37's contract, and the test compared that message
/// against a different one. The defect was the suite competing with itself for a connection, and it
/// is fixed where it lives rather than by loosening an assertion — the verbatim message is the one
/// thing this test exists to pin, so relaxing it would leave the test running and checking nothing.
/// </para>
/// <para>
/// A collection rather than disabling parallelism for the project: the REST classes are unaffected
/// and there are twenty-six of them, so serialising the whole tier would cost minutes per run to
/// fix a constraint that binds two files. Any future class that opens a stream joins this
/// collection.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SharedStocksSocket
{
    /// <summary>The collection name, referenced by every class that opens a stream.</summary>
    public const string Name = "live stocks stream";
}
