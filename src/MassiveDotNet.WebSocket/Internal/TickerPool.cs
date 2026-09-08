using System.Text.Json;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// A bounded intern table for ticker symbols, so a repeated symbol costs no allocation.
/// </summary>
/// <remarks>
/// <para>
/// The read loop is single-threaded by construction — one loop per connection — so nothing here
/// locks. Sharing an instance across connections would need one and is not done.
/// </para>
/// <para>
/// The cap is not a tuning knob but a correctness property: without it this is a cache that grows
/// with every distinct symbol ever seen and is never trimmed. Past the cap it stops adding and
/// returns freshly allocated strings, which is slower and bounded rather than faster and unbounded.
/// </para>
/// </remarks>
internal sealed class TickerPool
{
    // Comfortably above any real symbol, including crypto pairs such as X:BTC-USD. A value longer
    // than this is not a ticker, so it is not worth a pool slot.
    private const int MaxStackChars = 24;

    private readonly Dictionary<string, string> _pool;
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _capacity;

    public TickerPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _pool = new Dictionary<string, string>(StringComparer.Ordinal);
        _lookup = _pool.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>How many distinct symbols are held.</summary>
    public int Count => _pool.Count;

    /// <summary>Returns the pooled instance for <paramref name="ticker"/>, adding it if there is room.</summary>
    public string Intern(ReadOnlySpan<char> ticker)
    {
        // The alternate lookup is the point: probing by span means no string is allocated to ask
        // the question, so a hit costs nothing at all.
        if (_lookup.TryGetValue(ticker, out string? existing))
        {
            return existing;
        }

        string created = new(ticker);

        if (_pool.Count < _capacity)
        {
            _pool[created] = created;
        }

        return created;
    }

    /// <summary>Interns the string the reader is positioned on, without materializing a probe.</summary>
    /// <param name="reader">A reader positioned on a string token.</param>
    /// <returns>The pooled instance, or a fresh string when the value cannot be pooled.</returns>
    public string Intern(ref Utf8JsonReader reader)
    {
        // The UTF-8 byte count is an upper bound on the char count, so it is safe to size the
        // stack buffer from it: no encoding produces more chars than bytes here.
        long byteLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (byteLength > MaxStackChars)
        {
            return reader.GetString() ?? string.Empty;
        }

        Span<char> buffer = stackalloc char[MaxStackChars];
        int written = reader.CopyString(buffer);

        return Intern(buffer[..written]);
    }
}
