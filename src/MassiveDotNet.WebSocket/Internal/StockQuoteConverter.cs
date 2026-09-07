using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockQuote"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from. The shape follows D32's generated struct converters exactly: ordinal
/// matching, last value wins, unknown properties skipped wholesale, and scalars read through
/// <see cref="JsonValueReader"/> so a malformed value surfaces as a <see cref="JsonException"/>.
/// </remarks>
internal sealed class StockQuoteConverter(TickerPool tickers) : JsonConverter<StockQuote>
{
    private const string Model = nameof(StockQuote);

    public override StockQuote Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {Model}, but found a {reader.TokenType} token.");
        }

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

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("sym"u8))
            {
                reader.Read();
                ticker = tickers.Intern(ref reader);
            }
            else if (reader.ValueTextEquals("bx"u8))
            {
                reader.Read();
                bidExchangeId = JsonValueReader.ReadInt32(ref reader, Model, "bx");
            }
            else if (reader.ValueTextEquals("bp"u8))
            {
                reader.Read();
                bidPrice = JsonValueReader.ReadDouble(ref reader, Model, "bp");
            }
            else if (reader.ValueTextEquals("bs"u8))
            {
                reader.Read();
                bidSize = JsonValueReader.ReadInt64(ref reader, Model, "bs");
            }
            else if (reader.ValueTextEquals("ax"u8))
            {
                reader.Read();
                askExchangeId = JsonValueReader.ReadInt32(ref reader, Model, "ax");
            }
            else if (reader.ValueTextEquals("ap"u8))
            {
                reader.Read();
                askPrice = JsonValueReader.ReadDouble(ref reader, Model, "ap");
            }
            else if (reader.ValueTextEquals("as"u8))
            {
                reader.Read();
                askSize = JsonValueReader.ReadInt64(ref reader, Model, "as");
            }
            else if (reader.ValueTextEquals("c"u8))
            {
                reader.Read();
                condition = JsonValueReader.ReadNullableInt32(ref reader, Model, "c");
            }
            else if (reader.ValueTextEquals("i"u8))
            {
                reader.Read();
                // Indicators are the same wire shape as a trade's condition array.
                indicators = StockTradeConverter.ReadConditions(ref reader, Model, "i");
            }
            else if (reader.ValueTextEquals("t"u8))
            {
                reader.Read();
                sipTimestamp = JsonValueReader.ReadInt64(ref reader, Model, "t");
            }
            else if (reader.ValueTextEquals("q"u8))
            {
                reader.Read();
                sequenceNumber = JsonValueReader.ReadInt64(ref reader, Model, "q");
            }
            else if (reader.ValueTextEquals("z"u8))
            {
                reader.Read();
                tape = JsonValueReader.ReadNullableInt32(ref reader, Model, "z");
            }
            else
            {
                // Includes "ev", which the dispatcher has already consumed to choose this
                // converter, and anything Massive adds later.
                reader.Read();
                reader.Skip();
            }
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
        writer.WriteStartObject();
        writer.WriteString("ev", "Q");
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("bx", value.BidExchangeId);
        writer.WriteNumber("bp", value.BidPrice);
        writer.WriteNumber("bs", value.BidSize);
        writer.WriteNumber("ax", value.AskExchangeId);
        writer.WriteNumber("ap", value.AskPrice);
        writer.WriteNumber("as", value.AskSize);
        writer.WriteNumber("t", value.SipTimestampMilliseconds);
        writer.WriteNumber("q", value.SequenceNumber);
        writer.WriteEndObject();
    }
}
