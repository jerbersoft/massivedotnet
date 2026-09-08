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
/// <para>
/// Equality is hand-written rather than left to the compiler-synthesized member-wise comparison a
/// struct normally gets: the <see cref="InlineArrayAttribute"/> field makes the runtime's default
/// <c>ValueType.Equals</c>/<c>GetHashCode</c> refuse to run at all
/// (<see cref="NotSupportedException"/>), and <see cref="StockTrade"/> and <see cref="StockQuote"/>
/// are <see langword="record struct"/> types (D4) whose own synthesized equality delegates straight
/// to this type's. Without <see cref="IEquatable{T}"/> here, both events would inherit that failure.
/// </para>
/// </remarks>
public readonly struct ConditionSet : IEquatable<ConditionSet>
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
    /// The inline branch reads through <see cref="Unsafe.AsRef{T}(ref readonly T)"/> over a
    /// <see langword="readonly"/> field, which is what makes this method possible on a
    /// <see langword="readonly"/> struct at all: an <see cref="InlineArrayAttribute"/> buffer
    /// normally cannot be indexed, let alone spanned, through a <see langword="readonly"/> binding,
    /// because the compiler cannot prove indexing does not mutate it.
    /// <see cref="Unsafe.AsRef{T}(ref readonly T)"/> deliberately launders that ref-safety, which
    /// means the compiler can no longer catch a span that outlives the <see cref="ConditionSet"/> it
    /// was taken from.
    /// </para>
    /// <para>
    /// The two branches do not fail the same way, and the difference matters more than it looks.
    /// <b>Inline</b> (<see cref="Count"/> at most <see cref="InlineCapacity"/>): the returned span
    /// points at bytes living inside this <see cref="ConditionSet"/> value itself. Once that exact
    /// value is copied over, reassigned, or falls out of scope, the span is left pointing at
    /// whatever now occupies that memory — it does not throw, it reads garbage.
    /// <c>trade.Conditions.AsSpan().ToArray()</c> is safe, because the copy happens before the value
    /// could go stale; holding the span itself across statements, such as
    /// <c>ReadOnlySpan&lt;int&gt; s = trade.Conditions.AsSpan();</c> followed by later use, is not.
    /// <b>Spilled</b> (<see cref="Count"/> above <see cref="InlineCapacity"/>): the returned span is
    /// backed by the independently heap-allocated <c>int[]</c>, which the <see cref="ReadOnlySpan{T}"/>
    /// itself keeps referenced — so, measured, it keeps reading correctly even after the source
    /// <see cref="ConditionSet"/> is cleared or discarded. This is the more dangerous case, not the
    /// safer one: it is an accident of how many codes a given message happened to carry, not a
    /// documented guarantee, so code that holds the span because "it worked when I tried it with
    /// nine conditions" breaks the moment a message arrives with three. Nothing at the call site
    /// distinguishes which branch a given <see cref="ConditionSet"/> took, so treat both the same
    /// way: a caller that needs the codes to outlive the <see cref="ConditionSet"/> they came from
    /// must call <c>ToArray()</c> on the returned span, or use <see cref="this[int]"/>, rather than
    /// retain the span itself. This compiles clean either way, because the hazard is exactly the one
    /// this method exists to route around.
    /// </para>
    /// </remarks>
    public ReadOnlySpan<int> AsSpan() =>
        _overflow is { } overflow
            ? overflow.AsSpan(0, Count)
            : MemoryMarshal.CreateReadOnlySpan(
                in Unsafe.As<Buffer, int>(ref Unsafe.AsRef(in _inline)),
                Count);

    /// <summary>Whether this set holds the same codes, in the same order, as <paramref name="other"/>.</summary>
    /// <param name="other">The set to compare against.</param>
    /// <returns><see langword="true"/> when both sets hold the same codes in the same order.</returns>
    public bool Equals(ConditionSet other) => AsSpan().SequenceEqual(other.AsSpan());

    /// <summary>Whether <paramref name="obj"/> is a <see cref="ConditionSet"/> equal to this one.</summary>
    /// <param name="obj">The object to compare against.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal <see cref="ConditionSet"/>.</returns>
    public override bool Equals(object? obj) => obj is ConditionSet other && Equals(other);

    /// <summary>A hash code consistent with <see cref="Equals(ConditionSet)"/>: equal sets hash equal.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;

        foreach (int code in AsSpan())
        {
            hash.Add(code);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> hold the same codes in the same order.</summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> when the two sets are equal.</returns>
    public static bool operator ==(ConditionSet left, ConditionSet right) => left.Equals(right);

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> differ.</summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> when the two sets are not equal.</returns>
    public static bool operator !=(ConditionSet left, ConditionSet right) => !left.Equals(right);
}
