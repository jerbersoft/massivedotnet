using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Drives <see cref="PooledArrayConverter{T}"/> over a test-local source-generated context, for
/// the reason <see cref="InstantJsonConverterTests"/> gives: reflection-based serialization is off,
/// so a converter needs a context to resolve its element type through. The generated models are
/// covered end to end by the per-endpoint deserialization tests, which all route through this
/// converter once the generator emits it.
/// </summary>
/// <remarks>
/// The growth theory is the point of this file. A pooled buffer that doubles is the one place an
/// off-by-one silently truncates or duplicates a page of results, and the sizes below straddle
/// every boundary the doubling visits from the initial rent up past two grows.
/// </remarks>
public sealed class PooledArrayConverterTests
{
    private static readonly JsonSerializerOptions Options = PooledArrayTestContext.Default.Options;

    /// <summary>A value-type element, the case decision D4 makes common and issue #47 is about.</summary>
    internal readonly record struct Row
    {
        [JsonPropertyName("n")]
        public int Number { get; init; }

        [JsonPropertyName("v")]
        public double Value { get; init; }
    }

    /// <summary>A reference-type element, so the pool's clear-on-return path is exercised too.</summary>
    internal sealed record Label
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    /// <summary>Proves the converter leaves the reader on EndArray so an outer object keeps parsing.</summary>
    internal sealed record Bag
    {
        [JsonPropertyName("rows")]
        [JsonConverter(typeof(PooledArrayConverter<Row>))]
        public Row[]? Rows { get; init; }

        [JsonPropertyName("tail")]
        public string? Tail { get; init; }
    }

    private static T[]? Read<T>(string json)
    {
        PooledArrayConverter<T> converter = new();
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain at least one token.");
        return converter.Read(ref reader, typeof(T[]), Options);
    }

    [Fact]
    public void ReadsAnArrayOfValueTypeElements()
    {
        Row[]? rows = Read<Row>("""[{"n":1,"v":1.5},{"n":2,"v":2.5}]""");

        Assert.NotNull(rows);
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].Number);
        Assert.Equal(2.5, rows[1].Value);
    }

    [Fact]
    public void ReadsAnArrayOfReferenceTypeElements()
    {
        Label[]? labels = Read<Label>("""[{"text":"a"},{"text":"b"}]""");

        Assert.NotNull(labels);
        Assert.Equal(["a", "b"], labels.Select(label => label.Text));
    }

    [Fact]
    public void ReadsAnEmptyArray()
    {
        Assert.Empty(Read<Row>("[]")!);
    }

    [Fact]
    public void ReadsNullAsNull()
    {
        Assert.Null(Read<Row>("null"));
    }

    /// <summary>
    /// Every element must survive the copy out of the pooled buffer, at every size where the buffer
    /// is exactly full, one past full, and one short of full.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public void ReadsEveryElementAcrossGrowthBoundaries(int count)
    {
        StringBuilder json = new("[");

        for (int i = 0; i < count; i++)
        {
            json.Append(i == 0 ? "" : ",").Append($$"""{"n":{{i}},"v":{{i}}.5}""");
        }

        Row[]? rows = Read<Row>(json.Append(']').ToString());

        Assert.NotNull(rows);
        Assert.Equal(count, rows.Length);

        // Indexes, not just the count: a doubling copy that drops or repeats a block keeps the
        // length right while corrupting the contents.
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, rows[i].Number);
            Assert.Equal(i + 0.5, rows[i].Value);
        }
    }

    [Fact]
    public void LeavesTheReaderOnEndArraySoAnOuterObjectKeepsParsing()
    {
        Bag? bag = JsonSerializer.Deserialize(
            """{"rows":[{"n":1,"v":1.5},{"n":2,"v":2.5}],"tail":"after"}""",
            PooledArrayTestContext.Default.Bag);

        Assert.NotNull(bag);
        Assert.Equal(2, bag.Rows!.Length);
        Assert.Equal("after", bag.Tail);
    }

    [Fact]
    public void ThrowsOnATokenThatIsNotAnArray()
    {
        JsonException error = Assert.Throws<JsonException>(() => Read<Row>("""{"n":1}"""));
        Assert.Contains("StartObject", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAnArrayBack()
    {
        ArrayBufferWriter<byte> buffer = new();
        PooledArrayConverter<Row> converter = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            converter.Write(writer, [new Row { Number = 1, Value = 1.5 }], Options);
        }

        Assert.Equal("""[{"n":1,"v":1.5}]""", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}

[JsonSerializable(typeof(PooledArrayConverterTests.Row))]
[JsonSerializable(typeof(PooledArrayConverterTests.Label))]
[JsonSerializable(typeof(PooledArrayConverterTests.Bag))]
internal sealed partial class PooledArrayTestContext : JsonSerializerContext;
