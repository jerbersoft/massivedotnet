using System.Text.Json;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// Walks the properties of one streaming event object: skips any value the converter does not
/// consume, and reads every value it does consume through <see cref="JsonValueReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every streaming converter goes through this, so the two halves of a defect shape the #20
/// whole-branch review named have nowhere to live. A converter written against this has no
/// <c>else</c> arm to forget, because <see cref="NextProperty"/> performs the skip itself; and no
/// way to read a value off the raw reader, because it asks this type for the value instead. A
/// missed <c>Skip()</c> is worth this much care precisely because it does not throw: it leaves the
/// reader mid-value, so the next property is read from the wrong token and deserializes wrong.
/// </para>
/// <para>
/// A plain <see langword="struct" /> rather than a <c>ref struct</c>, and the reader travels as a
/// parameter rather than living in a field. Both alternatives are rejected by the compiler: a
/// <c>ref Utf8JsonReader</c> field is <c>CS9050</c>, a ref field cannot refer to a ref struct, and
/// a <c>ref struct</c> receiver taking one is <c>CS8350</c>, since such a receiver might capture
/// the reference. A plain struct cannot hold a ref field at all, so there is nothing left to
/// reject. The cost is <c>ref reader</c> at every call site.
/// </para>
/// </remarks>
internal struct StreamEventWalk
{
    private readonly string _model;

    // Whether the CURRENT property's value has been read. False means the walk is parked on a
    // property name whose value nobody wanted, and NextProperty owes it a skip. True at
    // construction because there is no previous property to skip.
    private bool _valueConsumed;

    /// <summary>Begins a walk over the object the reader is positioned on.</summary>
    /// <param name="reader">A reader positioned on the object's opening token.</param>
    /// <param name="model">The model being read, named in every failure message.</param>
    /// <exception cref="JsonException">The reader is not positioned on an object.</exception>
    public StreamEventWalk(ref Utf8JsonReader reader, string model)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {model}, but found a {reader.TokenType} token.");
        }

        _model = model;
        _valueConsumed = true;
    }

    /// <summary>
    /// Advances to the next property name, skipping the current property's value if no accessor
    /// consumed it.
    /// </summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <returns><see langword="false"/> at the end of the object.</returns>
    public bool NextProperty(ref Utf8JsonReader reader)
    {
        if (!_valueConsumed)
        {
            // Read moves onto the value; Skip walks past its whole subtree, which is what makes an
            // unrecognised object or array cost the same as an unrecognised scalar.
            reader.Read();
            reader.Skip();
        }

        _valueConsumed = false;

        return reader.Read() && reader.TokenType == JsonTokenType.PropertyName;
    }

    /// <summary>Reads a ticker symbol through the pool, so a repeat symbol allocates nothing.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="tickers">The pool to intern through.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The interned symbol.</returns>
    /// <exception cref="JsonException">The value is not a string.</exception>
    /// <remarks>
    /// <see cref="TickerPool.Intern(ref Utf8JsonReader)"/> reads the raw UTF-8 bytes directly for
    /// the pooling fast path, bypassing <see cref="JsonValueReader"/>, so the token check happens
    /// here instead: without it a null or numeric symbol would surface as
    /// <see cref="InvalidOperationException"/> rather than the <see cref="JsonException"/> every
    /// other field throws.
    /// </remarks>
    public string Ticker(ref Utf8JsonReader reader, TickerPool tickers, string property)
    {
        Advance(ref reader);

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string for {_model}.{property}, but found a {reader.TokenType} token.");
        }

        return tickers.Intern(ref reader);
    }

    /// <summary>Reads a string, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    public string? String(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadString(ref reader, _model, property);
    }

    /// <summary>Reads a boolean.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The boolean.</returns>
    public bool Boolean(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadBoolean(ref reader, _model, property);
    }

    /// <summary>Reads a 32-bit integer.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer.</returns>
    public int Int32(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadInt32(ref reader, _model, property);
    }

    /// <summary>Reads a 32-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    public int? NullableInt32(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadNullableInt32(ref reader, _model, property);
    }

    /// <summary>Reads a 64-bit integer.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer.</returns>
    public long Int64(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadInt64(ref reader, _model, property);
    }

    /// <summary>Reads a 64-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    public long? NullableInt64(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadNullableInt64(ref reader, _model, property);
    }

    /// <summary>Reads a double.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The double.</returns>
    public double Double(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadDouble(ref reader, _model, property);
    }

    /// <summary>Reads an integer code array into a <see cref="ConditionSet"/>.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The parsed set, empty from a JSON null.</returns>
    public ConditionSet Conditions(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return ConditionSetSerialization.Read(ref reader, _model, property);
    }

    private void Advance(ref Utf8JsonReader reader)
    {
        reader.Read();
        _valueConsumed = true;
    }
}
