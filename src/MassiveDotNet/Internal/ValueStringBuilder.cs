using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MassiveDotNet.Internal;

/// <summary>
/// A stack-allocated string builder that grows into pooled memory. Used to compose request
/// URIs without the intermediate allocations of <see cref="System.Text.StringBuilder"/> or
/// <see cref="UriBuilder"/>.
/// </summary>
internal ref struct ValueStringBuilder
{
    private char[]? _rented;
    private Span<char> _chars;
    private int _position;

    /// <summary>Initializes a builder over caller-supplied scratch space, typically stack allocated.</summary>
    /// <param name="initialBuffer">The initial buffer to write into before growing into the pool.</param>
    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _rented = null;
        _chars = initialBuffer;
        _position = 0;
    }

    /// <summary>The number of characters written so far.</summary>
    public readonly int Length => _position;

    /// <summary>Appends a single character.</summary>
    /// <param name="value">The character to append.</param>
    public void Append(char value)
    {
        if ((uint)_position >= (uint)_chars.Length)
        {
            Grow(1);
        }

        _chars[_position++] = value;
    }

    /// <summary>Appends a span of characters.</summary>
    /// <param name="value">The characters to append.</param>
    public void Append(scoped ReadOnlySpan<char> value)
    {
        if (value.Length > _chars.Length - _position)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars[_position..]);
        _position += value.Length;
    }

    /// <summary>Appends the invariant decimal representation of an integer.</summary>
    /// <param name="value">The value to append.</param>
    public void Append(int value)
    {
        if (!value.TryFormat(_chars[_position..], out int written, provider: null))
        {
            Grow(16);
            bool ok = value.TryFormat(_chars[_position..], out written, provider: null);
            Debug.Assert(ok, "Formatting an int into a freshly grown buffer should always succeed.");
        }

        _position += written;
    }

    /// <summary>Appends the invariant decimal representation of a 64-bit integer.</summary>
    /// <param name="value">The value to append.</param>
    public void Append(long value)
    {
        if (!value.TryFormat(_chars[_position..], out int written, provider: null))
        {
            Grow(24);
            bool ok = value.TryFormat(_chars[_position..], out written, provider: null);
            Debug.Assert(ok, "Formatting a long into a freshly grown buffer should always succeed.");
        }

        _position += written;
    }

    /// <summary>Appends the shortest round-trippable invariant representation of a double.</summary>
    /// <param name="value">The value to append.</param>
    /// <remarks>
    /// The provider is named explicitly: a query string is not user-facing text, and
    /// <c>0,5</c> under a comma-decimal culture would be a silent wrong request.
    /// </remarks>
    public void Append(double value)
    {
        if (!value.TryFormat(_chars[_position..], out int written, format: default, provider: CultureInfo.InvariantCulture))
        {
            Grow(32);
            bool ok = value.TryFormat(_chars[_position..], out written, format: default, provider: CultureInfo.InvariantCulture);
            Debug.Assert(ok, "Formatting a double into a freshly grown buffer should always succeed.");
        }

        _position += written;
    }

    /// <summary>Materializes the accumulated text and returns any pooled buffer.</summary>
    /// <returns>The composed string.</returns>
    public override string ToString()
    {
        string result = _chars[.._position].ToString();
        Dispose();
        return result;
    }

    /// <summary>Returns any pooled buffer without materializing a string.</summary>
    public void Dispose()
    {
        char[]? rented = _rented;
        this = default;

        if (rented is not null)
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additionalCapacity)
    {
        int required = Math.Max(_position + additionalCapacity, _chars.Length * 2);
        char[] next = ArrayPool<char>.Shared.Rent(required);

        _chars[.._position].CopyTo(next);

        char[]? previous = _rented;
        _chars = _rented = next;

        if (previous is not null)
        {
            ArrayPool<char>.Shared.Return(previous);
        }
    }
}
