using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The collection every process-wide memory measurement in this project joins, with
/// parallelization off.
/// </summary>
/// <remarks>
/// <c>CursorTraversalTests</c> already carried <c>[Collection("Process memory")]</c>, but a
/// collection attribute alone only keeps its members from running beside EACH OTHER -- the
/// <c>DisableParallelization</c> flag lives on the definition, and definitions are per-assembly, so
/// the one in <c>MassiveDotNet.WebSocket.Tests</c> did nothing here. That left this project relying
/// on a property of its own test suite rather than on a guard: <see cref="GC.GetTotalMemory(bool)"/>
/// is process-wide, and nothing polluted it only because these 497 tests are synchronous
/// stub-handler tests with nothing running on another thread. The first genuinely
/// background-threaded async test added to this project would have inherited the bug the WebSocket
/// project actually hit, where the same measurement reported up to 104 MB of phantom growth.
/// Found by the whole-branch review, 2026-09-08.
/// </remarks>
[CollectionDefinition("Process memory", DisableParallelization = true)]
public sealed class ProcessMemoryTests;
