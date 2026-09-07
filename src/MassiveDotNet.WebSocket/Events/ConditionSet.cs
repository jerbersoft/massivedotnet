using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>Up to eight condition codes held inline, spilling to the heap beyond that.</summary>
/// <remarks>
/// <para>
/// An <c>int[]</c> would allocate on every event carrying conditions, which is most trades. Renting
/// from <c>ArrayPool</c> is not available either: the array escapes to the caller with the event, so
/// nothing could ever return it. Eight covers every condition set observed, and the field holds no
/// reference in that case, so the common path allocates nothing.
/// </para>
/// <para>
/// A ninth code spills to a heap array rather than being dropped. Truncating would be silent data
/// loss, which this SDK refuses everywhere else.
/// </para>
/// </remarks>
public readonly struct ConditionSet
{
    /// <summary>How many codes fit before spilling to the heap.</summary>
    public const int InlineCapacity = 8;

    [InlineArray(InlineCapacity)]
    private struct Buffer
    {
        private int _element0;
    }

    private readonly Buffer _inline;
    private readonly int[]? _overflow;

    internal ConditionSet(ReadOnlySpan<int> codes)
    {
        Count = codes.Length;

        if (codes.Length > InlineCapacity)
        {
            _overflow = codes.ToArray();
            return;
        }

        for (int i = 0; i < codes.Length; i++)
        {
            _inline[i] = codes[i];
        }
    }

    /// <summary>How many codes this set holds.</summary>
    public int Count { get; }

    /// <summary>The code at <paramref name="index"/>.</summary>
    /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
    public int this[int index] => AsSpan()[index];

    /// <summary>The codes, as a span over inline or spilled storage.</summary>
    /// <returns>A span of exactly <see cref="Count"/> codes.</returns>
    /// <remarks>
    /// <para>
    /// The inline branch reads through <c>Unsafe.AsRef</c> over a <see langword="readonly"/> field,
    /// which is what makes this method possible on a <see langword="readonly"/> struct at all: an
    /// <see cref="InlineArrayAttribute"/> buffer normally cannot be indexed, let alone spanned,
    /// through a <see langword="readonly"/> binding, because the compiler cannot prove indexing does
    /// not mutate it. <c>Unsafe.AsRef</c> deliberately launders that ref-safety, which means the
    /// compiler can no longer catch a span that outlives the <see cref="ConditionSet"/> it was taken
    /// from.
    /// </para>
    /// <para>
    /// The span returned here is valid only while this <see cref="ConditionSet"/> instance is alive
    /// and has not been overwritten or gone out of scope. <c>trade.Conditions.AsSpan().ToArray()</c>
    /// is safe, because the copy happens before the value could go stale. Holding the span itself
    /// across statements, such as <c>ReadOnlySpan&lt;int&gt; s = trade.Conditions.AsSpan();</c>
    /// followed by later use, is not safe — the inline storage lives inside the
    /// <see cref="ConditionSet"/> value, and nothing stops that value from being replaced,
    /// reassigned, or falling out of scope in between, at which point the span reads whatever
    /// happens to occupy that memory next. This compiles clean either way, because the hazard is
    /// exactly the one this method exists to route around. A caller that needs the codes to outlive
    /// the <see cref="ConditionSet"/> they came from should call <c>ToArray()</c> on the returned
    /// span, or use <see cref="this[int]"/>, rather than retain the span itself.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<int> AsSpan() =>
        _overflow is { } overflow
            ? overflow.AsSpan(0, Count)
            : MemoryMarshal.CreateReadOnlySpan(
                in Unsafe.As<Buffer, int>(ref Unsafe.AsRef(in _inline)),
                Count);
}
