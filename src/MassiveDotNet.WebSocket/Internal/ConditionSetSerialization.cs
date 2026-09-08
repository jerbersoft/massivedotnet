using System.Text.Json;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// Reads and writes a <see cref="ConditionSet"/> against the token stream, shared by the trade and
/// quote converters.
/// </summary>
/// <remarks>
/// A trade's <c>c</c> and a quote's <c>i</c> are the same wire shape — a JSON array of integer
/// codes, or <see langword="null"/> — so one implementation serves both rather than each converter
/// carrying its own copy that could drift from the other.
/// </remarks>
internal static class ConditionSetSerialization
{
    /// <summary>Reads a code array into inline storage, spilling only past the inline capacity.</summary>
    /// <param name="reader">A reader positioned on the array's opening token, or a JSON null.</param>
    /// <param name="model">The model being read, named in a failure message.</param>
    /// <param name="property">The property being read, named in a failure message.</param>
    /// <returns>The parsed set.</returns>
    /// <exception cref="JsonException">The token is neither an array nor null, or a code is not an integer.</exception>
    internal static ConditionSet Read(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected an array for {model}.{property}, but found a {reader.TokenType} token.");
        }

        Span<int> inline = stackalloc int[ConditionSet.InlineCapacity];
        List<int>? spilled = null;
        int count = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int code = JsonValueReader.ReadInt32(ref reader, model, property);

            if (count < ConditionSet.InlineCapacity)
            {
                inline[count] = code;
            }
            else
            {
                spilled ??= [.. inline];
                spilled.Add(code);
            }

            count++;
        }

        return spilled is null ? new ConditionSet(inline[..count]) : new ConditionSet([.. spilled]);
    }

    /// <summary>
    /// Writes <paramref name="conditions"/> as a JSON array under <paramref name="property"/>,
    /// omitting the property entirely when the set is empty rather than writing an empty array —
    /// matching how an optional field is omitted rather than sent as empty everywhere else in this
    /// SDK.
    /// </summary>
    /// <param name="writer">The writer, positioned inside the enclosing object.</param>
    /// <param name="property">The wire property name.</param>
    /// <param name="conditions">The codes to write.</param>
    internal static void Write(Utf8JsonWriter writer, string property, ConditionSet conditions)
    {
        if (conditions.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(property);

        foreach (int code in conditions.AsSpan())
        {
            writer.WriteNumberValue(code);
        }

        writer.WriteEndArray();
    }
}
