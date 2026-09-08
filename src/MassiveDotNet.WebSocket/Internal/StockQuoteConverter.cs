using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockQuote"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk itself is <see cref="StreamEventWalk"/>, shared
/// with every other streaming converter, so ordinal matching, last value wins, wholesale skipping
/// of unknown properties, and scalars read through <see cref="JsonValueReader"/> are one
/// implementation rather than one per converter.
/// </remarks>
internal sealed class StockQuoteConverter(TickerPool tickers) : JsonConverter<StockQuote>
{
    private const string Model = nameof(StockQuote);

    public override StockQuote Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        int bidExchangeId = 0;
        double bidPrice = 0;
        long bidSize = 0;
        int askExchangeId = 0;
        double askPrice = 0;
        long askSize = 0;
        int? condition = null;
        ConditionSet indicators = default;
        long sipTimestamp = 0;
        long sequenceNumber = 0;
        int? tape = null;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("bx"u8)) { bidExchangeId = walk.Int32(ref reader, "bx"); }
            else if (reader.ValueTextEquals("bp"u8)) { bidPrice = walk.Double(ref reader, "bp"); }
            else if (reader.ValueTextEquals("bs"u8)) { bidSize = walk.Int64(ref reader, "bs"); }
            else if (reader.ValueTextEquals("ax"u8)) { askExchangeId = walk.Int32(ref reader, "ax"); }
            else if (reader.ValueTextEquals("ap"u8)) { askPrice = walk.Double(ref reader, "ap"); }
            else if (reader.ValueTextEquals("as"u8)) { askSize = walk.Int64(ref reader, "as"); }
            else if (reader.ValueTextEquals("c"u8)) { condition = walk.NullableInt32(ref reader, "c"); }
            // Indicators are the same wire shape as a trade's condition array.
            else if (reader.ValueTextEquals("i"u8)) { indicators = walk.Conditions(ref reader, "i"); }
            else if (reader.ValueTextEquals("t"u8)) { sipTimestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
        }

        return new StockQuote
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            BidExchangeId = bidExchangeId,
            BidPrice = bidPrice,
            BidSize = bidSize,
            AskExchangeId = askExchangeId,
            AskPrice = askPrice,
            AskSize = askSize,
            Condition = condition,
            Indicators = indicators,
            SipTimestampMilliseconds = sipTimestamp,
            SequenceNumber = sequenceNumber,
            Tape = tape,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockQuote value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null): a round trip that silently
        // dropped a field would be exactly the data loss this SDK refuses everywhere else.
        writer.WriteStartObject();
        writer.WriteString("ev", "Q");
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("bx", value.BidExchangeId);
        writer.WriteNumber("bp", value.BidPrice);
        writer.WriteNumber("bs", value.BidSize);
        writer.WriteNumber("ax", value.AskExchangeId);
        writer.WriteNumber("ap", value.AskPrice);
        writer.WriteNumber("as", value.AskSize);

        if (value.Condition is { } condition)
        {
            writer.WriteNumber("c", condition);
        }

        ConditionSetSerialization.Write(writer, "i", value.Indicators);
        writer.WriteNumber("t", value.SipTimestampMilliseconds);
        writer.WriteNumber("q", value.SequenceNumber);

        if (value.Tape is { } tape)
        {
            writer.WriteNumber("z", tape);
        }

        writer.WriteEndObject();
    }
}
