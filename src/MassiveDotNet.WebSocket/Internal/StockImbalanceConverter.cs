using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockImbalance"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter.
/// <para>
/// The ticker is read from <c>T</c>, which is this topic's spelling and is also the trade topic's
/// wire code. Nothing here reads <c>sym</c>.
/// </para>
/// </remarks>
internal sealed class StockImbalanceConverter(TickerPool tickers) : JsonConverter<StockImbalance>
{
    private const string Model = nameof(StockImbalance);

    public override StockImbalance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        long timestamp = 0;
        int auctionTimeCode = 0;
        string? auctionType = null;
        long symbolSequence = 0;
        int exchangeId = 0;
        long imbalanceQuantity = 0;
        long pairedQuantity = 0;
        double bookClearingPrice = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("T"u8)) { ticker = walk.Ticker(ref reader, tickers, "T"); }
            else if (reader.ValueTextEquals("t"u8)) { timestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("at"u8)) { auctionTimeCode = walk.Int32(ref reader, "at"); }
            else if (reader.ValueTextEquals("a"u8)) { auctionType = walk.String(ref reader, "a"); }
            else if (reader.ValueTextEquals("i"u8)) { symbolSequence = walk.Int64(ref reader, "i"); }
            else if (reader.ValueTextEquals("x"u8)) { exchangeId = walk.Int32(ref reader, "x"); }
            else if (reader.ValueTextEquals("o"u8)) { imbalanceQuantity = walk.Int64(ref reader, "o"); }
            else if (reader.ValueTextEquals("p"u8)) { pairedQuantity = walk.Int64(ref reader, "p"); }
            else if (reader.ValueTextEquals("b"u8)) { bookClearingPrice = walk.Double(ref reader, "b"); }
        }

        return new StockImbalance
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'T'."),
            TimestampNanoseconds = timestamp,
            AuctionTimeCode = auctionTimeCode,
            AuctionType = auctionType,
            SymbolSequence = symbolSequence,
            ExchangeId = exchangeId,
            ImbalanceQuantity = imbalanceQuantity,
            PairedQuantity = pairedQuantity,
            BookClearingPrice = bookClearingPrice,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockImbalance value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null).
        writer.WriteStartObject();
        writer.WriteString("ev", "NOI");
        writer.WriteString("T", value.Ticker);
        writer.WriteNumber("t", value.TimestampNanoseconds);
        writer.WriteNumber("at", value.AuctionTimeCode);

        if (value.AuctionType is { } auctionType)
        {
            writer.WriteString("a", auctionType);
        }

        writer.WriteNumber("i", value.SymbolSequence);
        writer.WriteNumber("x", value.ExchangeId);
        writer.WriteNumber("o", value.ImbalanceQuantity);
        writer.WriteNumber("p", value.PairedQuantity);
        writer.WriteNumber("b", value.BookClearingPrice);
        writer.WriteEndObject();
    }
}
