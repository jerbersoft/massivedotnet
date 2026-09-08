using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockLimitUpLimitDown"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter. The ticker is read from <c>T</c>; nothing here reads <c>sym</c>.
/// </remarks>
internal sealed class StockLimitUpLimitDownConverter(TickerPool tickers)
    : JsonConverter<StockLimitUpLimitDown>
{
    private const string Model = nameof(StockLimitUpLimitDown);

    public override StockLimitUpLimitDown Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        double highPrice = 0;
        double lowPrice = 0;
        ConditionSet indicators = default;
        int? tape = null;
        long timestamp = 0;
        long sequenceNumber = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("T"u8)) { ticker = walk.Ticker(ref reader, tickers, "T"); }
            else if (reader.ValueTextEquals("h"u8)) { highPrice = walk.Double(ref reader, "h"); }
            else if (reader.ValueTextEquals("l"u8)) { lowPrice = walk.Double(ref reader, "l"); }
            else if (reader.ValueTextEquals("i"u8)) { indicators = walk.Conditions(ref reader, "i"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
            else if (reader.ValueTextEquals("t"u8)) { timestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
        }

        return new StockLimitUpLimitDown
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'T'."),
            HighPrice = highPrice,
            LowPrice = lowPrice,
            Indicators = indicators,
            Tape = tape,
            TimestampNanoseconds = timestamp,
            SequenceNumber = sequenceNumber,
        };
    }

    public override void Write(
        Utf8JsonWriter writer, StockLimitUpLimitDown value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null).
        writer.WriteStartObject();
        writer.WriteString("ev", "LULD");
        writer.WriteString("T", value.Ticker);
        writer.WriteNumber("h", value.HighPrice);
        writer.WriteNumber("l", value.LowPrice);
        ConditionSetSerialization.Write(writer, "i", value.Indicators);

        if (value.Tape is { } tape)
        {
            writer.WriteNumber("z", tape);
        }

        writer.WriteNumber("t", value.TimestampNanoseconds);
        writer.WriteNumber("q", value.SequenceNumber);
        writer.WriteEndObject();
    }
}
