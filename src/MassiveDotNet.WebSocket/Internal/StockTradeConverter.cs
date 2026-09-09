using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockTrade"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk itself is <see cref="StreamEventWalk"/>, shared
/// with every other streaming converter, so ordinal matching, last value wins, wholesale skipping
/// of unknown properties, and scalars read through <see cref="JsonValueReader"/> are one
/// implementation rather than one per converter.
/// </remarks>
internal sealed class StockTradeConverter(TickerPool tickers) : JsonConverter<StockTrade>
{
    private const string Model = nameof(StockTrade);

    public override StockTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        string? tradeId = null;
        int exchangeId = 0;
        int? tape = null;
        double price = 0;
        long size = 0;
        decimal? decimalSize = null;
        ConditionSet conditions = default;
        long sipTimestamp = 0;
        long? participantTimestamp = null;
        long sequenceNumber = 0;
        int? trfId = null;
        long? trfTimestamp = null;

        // Every property the wire may carry, and nothing else: "ev" and anything Massive adds later
        // are skipped by the walk itself, so this loop has no default arm to get wrong.
        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("i"u8)) { tradeId = walk.String(ref reader, "i"); }
            else if (reader.ValueTextEquals("x"u8)) { exchangeId = walk.Int32(ref reader, "x"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
            else if (reader.ValueTextEquals("p"u8)) { price = walk.Double(ref reader, "p"); }
            else if (reader.ValueTextEquals("s"u8)) { size = walk.Int64(ref reader, "s"); }
            else if (reader.ValueTextEquals("ds"u8)) { decimalSize = walk.NullableDecimal(ref reader, "ds"); }
            else if (reader.ValueTextEquals("c"u8)) { conditions = walk.Conditions(ref reader, "c"); }
            else if (reader.ValueTextEquals("t"u8)) { sipTimestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("pt"u8)) { participantTimestamp = walk.NullableInt64(ref reader, "pt"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
            else if (reader.ValueTextEquals("trfi"u8)) { trfId = walk.NullableInt32(ref reader, "trfi"); }
            else if (reader.ValueTextEquals("trft"u8)) { trfTimestamp = walk.NullableInt64(ref reader, "trft"); }
        }

        return new StockTrade
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            TradeId = tradeId ?? throw new JsonException($"{Model} carried no 'i'."),
            ExchangeId = exchangeId,
            Tape = tape,
            Price = price,
            Size = size,
            DecimalSize = decimalSize,
            Conditions = conditions,
            SipTimestampMilliseconds = sipTimestamp,
            ParticipantTimestampMilliseconds = participantTimestamp,
            SequenceNumber = sequenceNumber,
            TrfId = trfId,
            TrfTimestampMilliseconds = trfTimestamp,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockTrade value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null): a round trip that silently
        // dropped a field would be exactly the data loss this SDK refuses everywhere else.
        writer.WriteStartObject();
        writer.WriteString("ev", "T");
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("x", value.ExchangeId);

        if (value.Tape is { } tape)
        {
            writer.WriteNumber("z", tape);
        }

        writer.WriteString("i", value.TradeId);
        writer.WriteNumber("p", value.Price);
        writer.WriteNumber("s", value.Size);

        if (value.DecimalSize is { } decimalSize)
        {
            JsonValueWriter.WriteDecimalString(writer, "ds", decimalSize);
        }

        ConditionSetSerialization.Write(writer, "c", value.Conditions);
        writer.WriteNumber("t", value.SipTimestampMilliseconds);

        if (value.ParticipantTimestampMilliseconds is { } participantTimestamp)
        {
            writer.WriteNumber("pt", participantTimestamp);
        }

        writer.WriteNumber("q", value.SequenceNumber);

        if (value.TrfId is { } trfId)
        {
            writer.WriteNumber("trfi", trfId);
        }

        if (value.TrfTimestampMilliseconds is { } trfTimestamp)
        {
            writer.WriteNumber("trft", trfTimestamp);
        }

        writer.WriteEndObject();
    }
}
