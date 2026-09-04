using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads a JSON array into a pooled buffer and copies it out once, so materializing a large result
/// page allocates the array it returns and nothing else.
/// </summary>
/// <typeparam name="T">The element type, resolved through the serializer's source-generated context.</typeparam>
/// <remarks>
/// <para>
/// <see cref="System.Text.Json"/> builds an array by filling a <see cref="List{T}"/> and calling
/// <c>ToArray</c>. <see cref="List{T}"/> doubles its backing array as it grows, so every
/// intermediate buffer is discarded. For a class element each discarded slot is an 8-byte
/// reference and the churn disappears against the objects themselves; for an 88-byte
/// <c>readonly record struct</c> row (decision D4) each slot is the whole value, and a
/// 50,000-row response allocated 8.8 times the array it returned.
/// </para>
/// <para>
/// Renting the growth buffers instead means they are reused rather than collected, which leaves
/// the returned array as very nearly the only allocation on the path. The buffer is returned in a
/// <c>finally</c> so a malformed body cannot leak it out of the pool, and cleared on return only
/// when <typeparamref name="T"/> carries references, since clearing a block of numeric structs
/// costs a memset for no benefit.
/// </para>
/// <para>
/// Applied by the generator as a property attribute rather than registered as a
/// <see cref="JsonConverterFactory"/>: a factory has to build its converter through
/// <c>MakeGenericType</c>, which is reflection over a generic instantiation the trimmer cannot
/// see, and constitution rules 3 and 4 rule that out. An attribute names the closed type at
/// compile time, so every instantiation is rooted in the generated code.
/// </para>
/// </remarks>
public sealed class PooledArrayConverter<T> : JsonConverter<T[]>
{
    // The pool rounds a request up to its own bucket size, so this is a floor rather than an exact
    // capacity. Small enough not to over-rent a two-element page, large enough that the common
    // small results never grow at all.
    private const int InitialCapacity = 16;

    /// <inheritdoc />
    public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A property typed T[]? short-circuits on null before reaching a converter, so this is
        // reached only when a converter is invoked directly.
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"Expected an array of {typeof(T).Name}, found a {reader.TokenType} token.");
        }

        JsonTypeInfo<T> elementInfo = ElementInfo(options);
        T[] buffer = ArrayPool<T>.Shared.Rent(InitialCapacity);
        int count = 0;

        try
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (count == buffer.Length)
                {
                    T[] larger = ArrayPool<T>.Shared.Rent(buffer.Length * 2);
                    Array.Copy(buffer, larger, count);
                    Return(buffer);
                    buffer = larger;
                }

                buffer[count++] = JsonSerializer.Deserialize(ref reader, elementInfo)!;
            }

            // An empty result is common enough to be worth not allocating for: every unknown-ticker
            // probe and every exhausted page ends here.
            return count == 0 ? [] : buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            Return(buffer);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        JsonTypeInfo<T> elementInfo = ElementInfo(options);

        writer.WriteStartArray();

        foreach (T element in value)
        {
            JsonSerializer.Serialize(writer, element, elementInfo);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Resolves the element's source-generated metadata from the options in play.
    /// </summary>
    /// <param name="options">The options the serializer is running under.</param>
    /// <returns>The element type's metadata.</returns>
    /// <exception cref="JsonException">
    /// The element type is absent from the context, which means the generator emitted this
    /// converter for a type it did not also make serializable.
    /// </exception>
    private static JsonTypeInfo<T> ElementInfo(JsonSerializerOptions options) =>
        options.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
            ?? throw new JsonException(
                $"No source-generated metadata is registered for {typeof(T).Name}.");

    private static void Return(T[] buffer) =>
        ArrayPool<T>.Shared.Return(buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
}
