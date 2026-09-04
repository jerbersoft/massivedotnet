using System.Text.Json;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads a scalar from a <see cref="Utf8JsonReader"/> positioned on its value token, reporting a
/// malformed value as a <see cref="JsonException"/>.
/// </summary>
/// <remarks>
/// <para>
/// The generated model converters are built from these. <see cref="Utf8JsonReader"/>'s own
/// accessors are the wrong shape for that job: <see cref="Utf8JsonReader.GetInt32"/> throws
/// <see cref="InvalidOperationException"/> on a token of the wrong kind and
/// <see cref="FormatException"/> on a number outside the target's range. Neither is a
/// <see cref="JsonException"/>, so neither is recognised as a malformed body by the transport, and
/// both would surface from an ordinary REST call as an exception type nothing documents.
/// </para>
/// <para>
/// One implementation rather than the same token check emitted into every generated converter,
/// for the reason decision D15 keeps filter rendering in <c>RequestUriBuilder</c>: it is tested
/// once here instead of once per generated file, and it cannot drift between them.
/// </para>
/// <para>
/// Every method leaves the reader on the value it read, so the caller's next
/// <see cref="Utf8JsonReader.Read"/> advances to the following property name. No method echoes the
/// offending value into its message; the model and property name are enough to locate it, and the
/// response body is not ours to quote back.
/// </para>
/// </remarks>
public static class JsonValueReader
{
    /// <summary>Reads a string, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a string nor null.</exception>
    public static string? ReadString(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw Expected("a string", reader.TokenType, model, property),
        };

    /// <summary>Reads a boolean.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The boolean.</returns>
    /// <exception cref="JsonException">The token is not <c>true</c> or <c>false</c>.</exception>
    public static bool ReadBoolean(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw Expected("a boolean", reader.TokenType, model, property),
        };

    /// <summary>Reads a boolean, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The boolean, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a boolean nor null.</exception>
    public static bool? ReadNullableBoolean(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadBoolean(ref reader, model, property);

    /// <summary>Reads a 32-bit integer.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit an <see cref="int"/>.</exception>
    public static int ReadInt32(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetInt32(out int value) ? value : throw OutOfRange("a 32-bit integer", model, property);
    }

    /// <summary>Reads a 32-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static int? ReadNullableInt32(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadInt32(ref reader, model, property);

    /// <summary>Reads a 64-bit integer.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit a <see cref="long"/>.</exception>
    /// <remarks>
    /// The range check matters more here than anywhere else in this file: nanosecond timestamps are
    /// stored raw as <see cref="long"/> (decision D5), and a value one past the range silently
    /// becoming a <see cref="double"/> would hand a caller a wrong instant with nothing to notice it.
    /// </remarks>
    public static long ReadInt64(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetInt64(out long value) ? value : throw OutOfRange("a 64-bit integer", model, property);
    }

    /// <summary>Reads a 64-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static long? ReadNullableInt64(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadInt64(ref reader, model, property);

    /// <summary>Reads a double.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The double.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit a <see cref="double"/>.</exception>
    public static double ReadDouble(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetDouble(out double value) ? value : throw OutOfRange("a double", model, property);
    }

    /// <summary>Reads a double, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The double, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static double? ReadNullableDouble(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadDouble(ref reader, model, property);

    private static JsonException Expected(string expected, JsonTokenType found, string model, string property) =>
        new($"Expected {expected} for {model}.{property}, but the response carried a {found} token.");

    private static JsonException OutOfRange(string expected, string model, string property) =>
        new($"The number in {model}.{property} does not fit {expected}.");
}
