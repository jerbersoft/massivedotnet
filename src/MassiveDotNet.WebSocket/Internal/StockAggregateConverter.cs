using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockAggregate"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter.
/// <para>
/// One converter serves both aggregate topics (D-W14), so the wire code it writes back is a
/// constructor argument rather than a constant: the <c>A</c> and <c>AM</c> payloads are identical
/// apart from that one field, and <c>Read</c> never consults it, since the dispatcher has already
/// used <c>ev</c> to choose this sink.
/// </para>
/// </remarks>
internal sealed class StockAggregateConverter(TickerPool tickers, string topicCode)
    : JsonConverter<StockAggregate>
{
    private const string Model = nameof(StockAggregate);

    public override StockAggregate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        long volume = 0;
        string? decimalVolume = null;
        long accumulatedVolume = 0;
        string? decimalAccumulatedVolume = null;
        double officialOpenPrice = 0;
        double volumeWeightedAveragePrice = 0;
        double open = 0;
        double close = 0;
        double high = 0;
        double low = 0;
        double dailyVolumeWeightedAveragePrice = 0;
        long averageTradeSize = 0;
        long start = 0;
        long end = 0;
        bool otc = false;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("v"u8)) { volume = walk.Int64(ref reader, "v"); }
            else if (reader.ValueTextEquals("dv"u8)) { decimalVolume = walk.String(ref reader, "dv"); }
            else if (reader.ValueTextEquals("av"u8)) { accumulatedVolume = walk.Int64(ref reader, "av"); }
            else if (reader.ValueTextEquals("dav"u8)) { decimalAccumulatedVolume = walk.String(ref reader, "dav"); }
            else if (reader.ValueTextEquals("op"u8)) { officialOpenPrice = walk.Double(ref reader, "op"); }
            else if (reader.ValueTextEquals("vw"u8)) { volumeWeightedAveragePrice = walk.Double(ref reader, "vw"); }
            else if (reader.ValueTextEquals("o"u8)) { open = walk.Double(ref reader, "o"); }
            else if (reader.ValueTextEquals("c"u8)) { close = walk.Double(ref reader, "c"); }
            else if (reader.ValueTextEquals("h"u8)) { high = walk.Double(ref reader, "h"); }
            else if (reader.ValueTextEquals("l"u8)) { low = walk.Double(ref reader, "l"); }
            else if (reader.ValueTextEquals("a"u8)) { dailyVolumeWeightedAveragePrice = walk.Double(ref reader, "a"); }
            else if (reader.ValueTextEquals("z"u8)) { averageTradeSize = walk.Int64(ref reader, "z"); }
            else if (reader.ValueTextEquals("s"u8)) { start = walk.Int64(ref reader, "s"); }
            else if (reader.ValueTextEquals("e"u8)) { end = walk.Int64(ref reader, "e"); }
            else if (reader.ValueTextEquals("otc"u8)) { otc = walk.Boolean(ref reader, "otc"); }
        }

        return new StockAggregate
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            Volume = volume,
            DecimalVolume = decimalVolume,
            AccumulatedVolume = accumulatedVolume,
            DecimalAccumulatedVolume = decimalAccumulatedVolume,
            OfficialOpenPrice = officialOpenPrice,
            VolumeWeightedAveragePrice = volumeWeightedAveragePrice,
            Open = open,
            Close = close,
            High = high,
            Low = low,
            DailyVolumeWeightedAveragePrice = dailyVolumeWeightedAveragePrice,
            AverageTradeSize = averageTradeSize,
            StartTimestampMilliseconds = start,
            EndTimestampMilliseconds = end,
            Otc = otc,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockAggregate value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null): a round trip that silently
        // dropped a field would be exactly the data loss this SDK refuses everywhere else. "otc" is
        // written only when true, because the wire's own convention is to omit it when false.
        writer.WriteStartObject();
        writer.WriteString("ev", topicCode);
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("v", value.Volume);

        if (value.DecimalVolume is { } decimalVolume)
        {
            writer.WriteString("dv", decimalVolume);
        }

        writer.WriteNumber("av", value.AccumulatedVolume);

        if (value.DecimalAccumulatedVolume is { } decimalAccumulatedVolume)
        {
            writer.WriteString("dav", decimalAccumulatedVolume);
        }

        writer.WriteNumber("op", value.OfficialOpenPrice);
        writer.WriteNumber("vw", value.VolumeWeightedAveragePrice);
        writer.WriteNumber("o", value.Open);
        writer.WriteNumber("c", value.Close);
        writer.WriteNumber("h", value.High);
        writer.WriteNumber("l", value.Low);
        writer.WriteNumber("a", value.DailyVolumeWeightedAveragePrice);
        writer.WriteNumber("z", value.AverageTradeSize);
        writer.WriteNumber("s", value.StartTimestampMilliseconds);
        writer.WriteNumber("e", value.EndTimestampMilliseconds);

        if (value.Otc)
        {
            writer.WriteBoolean("otc", true);
        }

        writer.WriteEndObject();
    }
}
