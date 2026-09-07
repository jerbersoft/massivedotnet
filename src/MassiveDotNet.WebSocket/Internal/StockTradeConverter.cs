using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockTrade"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from. The shape follows D32's generated struct converters exactly: ordinal
/// matching, last value wins, unknown properties skipped wholesale, and scalars read through
/// <see cref="JsonValueReader"/> so a malformed value surfaces as a <see cref="JsonException"/>.
/// </remarks>
internal sealed class StockTradeConverter(TickerPool tickers) : JsonConverter<StockTrade>
{
    private const string Model = nameof(StockTrade);

    public override StockTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {Model}, but found a {reader.TokenType} token.");
        }

        string? ticker = null;
        string? tradeId = null;
        int exchangeId = 0;
        int? tape = null;
        double price = 0;
        long size = 0;
        string? decimalSize = null;
        ConditionSet conditions = default;
        long sipTimestamp = 0;
        long? participantTimestamp = null;
        long sequenceNumber = 0;
        int? trfId = null;
        long? trfTimestamp = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("sym"u8))
            {
                reader.Read();
                ticker = tickers.Intern(ref reader);
            }
            else if (reader.ValueTextEquals("i"u8))
            {
                reader.Read();
                tradeId = JsonValueReader.ReadString(ref reader, Model, "i");
            }
            else if (reader.ValueTextEquals("x"u8))
            {
                reader.Read();
                exchangeId = JsonValueReader.ReadInt32(ref reader, Model, "x");
            }
            else if (reader.ValueTextEquals("z"u8))
            {
                reader.Read();
                tape = JsonValueReader.ReadNullableInt32(ref reader, Model, "z");
            }
            else if (reader.ValueTextEquals("p"u8))
            {
                reader.Read();
                price = JsonValueReader.ReadDouble(ref reader, Model, "p");
            }
            else if (reader.ValueTextEquals("s"u8))
            {
                reader.Read();
                size = JsonValueReader.ReadInt64(ref reader, Model, "s");
            }
            else if (reader.ValueTextEquals("ds"u8))
            {
                reader.Read();
                decimalSize = JsonValueReader.ReadString(ref reader, Model, "ds");
            }
            else if (reader.ValueTextEquals("c"u8))
            {
                reader.Read();
                conditions = ReadConditions(ref reader, Model, "c");
            }
            else if (reader.ValueTextEquals("t"u8))
            {
                reader.Read();
                sipTimestamp = JsonValueReader.ReadInt64(ref reader, Model, "t");
            }
            else if (reader.ValueTextEquals("pt"u8))
            {
                reader.Read();
                participantTimestamp = JsonValueReader.ReadNullableInt64(ref reader, Model, "pt");
            }
            else if (reader.ValueTextEquals("q"u8))
            {
                reader.Read();
                sequenceNumber = JsonValueReader.ReadInt64(ref reader, Model, "q");
            }
            else if (reader.ValueTextEquals("trfi"u8))
            {
                reader.Read();
                trfId = JsonValueReader.ReadNullableInt32(ref reader, Model, "trfi");
            }
            else if (reader.ValueTextEquals("trft"u8))
            {
                reader.Read();
                trfTimestamp = JsonValueReader.ReadNullableInt64(ref reader, Model, "trft");
            }
            else
            {
                // Includes "ev", which the dispatcher has already consumed to choose this
                // converter, and anything Massive adds later.
                reader.Read();
                reader.Skip();
            }
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

    /// <summary>Reads a code array into inline storage, spilling only past the inline capacity.</summary>
    internal static ConditionSet ReadConditions(ref Utf8JsonReader reader, string model, string property)
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

    public override void Write(Utf8JsonWriter writer, StockTrade value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("ev", "T");
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("x", value.ExchangeId);
        writer.WriteString("i", value.TradeId);
        writer.WriteNumber("p", value.Price);
        writer.WriteNumber("s", value.Size);
        writer.WriteNumber("t", value.SipTimestampMilliseconds);
        writer.WriteNumber("q", value.SequenceNumber);
        writer.WriteEndObject();
    }
}
