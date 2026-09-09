using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The wire contract every struct model honours, pinned against <see cref="Trade"/> because it
/// carries one of each shape the closed set allows: a required string, plain and nullable
/// <see cref="long"/> and <see cref="int"/>, a <see cref="double"/>, and an <see cref="int"/> array.
/// </summary>
/// <remarks>
/// <para>
/// These were written against System.Text.Json's own object machinery and must keep passing once
/// the generator emits a hand-written converter per struct model (issue #47). That is the whole
/// point of the file: the converter exists to change what deserialization <em>allocates</em>, and
/// nothing about what it <em>accepts</em> or <em>rejects</em>. A performance change that quietly
/// starts tolerating a malformed body, or stops tolerating a valid one, is a behaviour change
/// wearing a benchmark's clothing.
/// </para>
/// <para>
/// Driven through the public API rather than a converter in isolation, so the assertions cover the
/// composition the SDK actually ships -- envelope, pooled array, element -- and not a converter
/// invoked in a way no caller can reproduce.
/// </para>
/// </remarks>
public sealed class StructModelJsonContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>What a consumer serializing a model would have: their own options, not the SDK's context.</summary>
    private static readonly JsonSerializerOptions RoundTrip = new();

    /// <summary>A complete row, every property present and distinguishable from its neighbours.</summary>
    private const string CompleteRow = """
        {"id":"t1","sip_timestamp":1517562000016036600,"participant_timestamp":1517562000015577000,
         "trf_timestamp":1517562000015500000,"sequence_number":1063,"price":170.15,"size":2,
         "decimal_size":"2.0","exchange":11,"trf_id":2,"conditions":[14,41],"correction":1,"tape":3}
        """;

    private static async Task<Trade[]> ReadAsync(string rows)
    {
        InlineStubHandler handler = new($$"""{"status":"OK","results":[{{rows}}]}""");
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        return (await client.Stocks.ListTradesAsync("AAPL", cancellationToken: Ct)).Results;
    }

    private static async Task<JsonException> RejectsAsync(string rows)
    {
        MassiveApiException thrown = await Assert.ThrowsAsync<MassiveApiException>(() => ReadAsync(rows));

        // The transport wraps a malformed body rather than letting JsonException escape, so the
        // assertion has to reach through it to say which failure actually occurred.
        return Assert.IsAssignableFrom<JsonException>(thrown.InnerException);
    }

    [Fact]
    public async Task BindsEveryPropertyFromItsWireName()
    {
        Trade trade = Assert.Single(await ReadAsync(CompleteRow));

        Assert.Equal("t1", trade.TradeId);
        Assert.Equal(1517562000016036600, trade.SipTimestampNanoseconds);
        Assert.Equal(1517562000015577000, trade.ParticipantTimestampNanoseconds);
        Assert.Equal(1517562000015500000, trade.TrfTimestampNanoseconds);
        Assert.Equal(1063, trade.SequenceNumber);
        Assert.Equal(170.15, trade.Price);
        Assert.Equal(2, trade.Size);
        Assert.Equal(2.0m, trade.DecimalSize);
        Assert.Equal(11, trade.ExchangeId);
        Assert.Equal(2, trade.TrfId);
        Assert.Equal<int>([14, 41], trade.Conditions!);
        Assert.Equal(1, trade.CorrectionIndicator);
        Assert.Equal(3, trade.Tape);
    }

    /// <summary>
    /// Wire order is the server's choice, not the schema's. Reversing it must change nothing, which
    /// is the property a positional reader would fail and a name-matching one cannot.
    /// </summary>
    [Fact]
    public async Task IsIndifferentToPropertyOrder()
    {
        Trade trade = Assert.Single(await ReadAsync("""
            {"tape":3,"correction":1,"conditions":[14,41],"trf_id":2,"exchange":11,"decimal_size":"2.0",
             "size":2,"price":170.15,"sequence_number":1063,"trf_timestamp":1517562000015500000,
             "participant_timestamp":1517562000015577000,"sip_timestamp":1517562000016036600,"id":"t1"}
            """));

        Assert.Equal("t1", trade.TradeId);
        Assert.Equal(1517562000016036600, trade.SipTimestampNanoseconds);
        Assert.Equal<int>([14, 41], trade.Conditions!);
        Assert.Equal(3, trade.Tape);
    }

    /// <summary>An absent optional property takes its default; the required ones are still present.</summary>
    [Fact]
    public async Task LeavesAbsentOptionalPropertiesAtTheirDefault()
    {
        Trade trade = Assert.Single(await ReadAsync("""{"id":"t1","decimal_size":"2.0"}"""));

        Assert.Equal(0, trade.SipTimestampNanoseconds);
        Assert.Equal(0, trade.Price);
        Assert.Null(trade.TrfTimestampNanoseconds);
        Assert.Null(trade.Tape);
        Assert.Null(trade.Conditions);
    }

    /// <summary>An explicit null is the absent case for a nullable, and for an array alike.</summary>
    [Fact]
    public async Task ReadsAnExplicitNullAsAbsent()
    {
        Trade trade = Assert.Single(await ReadAsync(
            """{"id":"t1","decimal_size":"2.0","trf_timestamp":null,"tape":null,"conditions":null}"""));

        Assert.Null(trade.TrfTimestampNanoseconds);
        Assert.Null(trade.Tape);
        Assert.Null(trade.Conditions);
    }

    /// <summary>
    /// The service adds fields without warning, so an unknown one is skipped rather than rejected.
    /// The nested cases are the ones a hand-written reader gets wrong: an unknown object whose own
    /// keys collide with real property names will bind them unless the value is skipped wholesale.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"t1","decimal_size":"2.0","brand_new":7}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","brand_new":[1,2,3]}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","brand_new":{"price":999.0,"tape":9}}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","brand_new":{"a":{"b":[{"tape":9}]}},"tape":null}""")]
    public async Task SkipsAnUnknownPropertyWholesale(string row)
    {
        Trade trade = Assert.Single(await ReadAsync(row));

        Assert.Equal("t1", trade.TradeId);
        Assert.Equal(0, trade.Price);
        Assert.Null(trade.Tape);
    }

    /// <summary>Matching is ordinal and case-sensitive, so a differently-cased key is unknown.</summary>
    [Fact]
    public async Task DoesNotMatchAPropertyNameByCase()
    {
        Trade trade = Assert.Single(await ReadAsync("""{"id":"t1","decimal_size":"2.0","TAPE":3,"Price":1.5}"""));

        Assert.Null(trade.Tape);
        Assert.Equal(0, trade.Price);
    }

    /// <summary>A repeated key takes the last value, which is what a single forward pass produces.</summary>
    [Fact]
    public async Task TakesTheLastValueOfARepeatedProperty()
    {
        Trade trade = Assert.Single(await ReadAsync(
            """{"id":"t1","decimal_size":"2.0","tape":1,"tape":2,"tape":3}"""));

        Assert.Equal(3, trade.Tape);
    }

    /// <summary>An empty array is an empty page, not a null one.</summary>
    [Fact]
    public async Task ReadsAnEmptyArrayAsAnEmptyPage() => Assert.Empty(await ReadAsync(""));

    /// <summary>
    /// A missing required reference property is rejected. The schema calls these fields required,
    /// and the SDK's own signature says <c>string</c>, not <c>string?</c>; accepting the row would
    /// hand a caller a null through a non-nullable property.
    /// </summary>
    [Theory]
    [InlineData("""{"decimal_size":"2.0"}""")]
    [InlineData("{}")]
    public async Task RejectsARowMissingARequiredReferenceProperty(string row) => await RejectsAsync(row);

    /// <summary>
    /// A required value-typed property carries no presence check, so an absent one reads as its
    /// default rather than throwing.
    /// </summary>
    /// <remarks>
    /// This is the rule every number on a struct model has always followed -- <c>price</c> below is
    /// required by the description too -- and <c>decimal_size</c> joined them when it stopped being
    /// a <see cref="string"/> (D38). The check it lost was never backed by anything trustworthy:
    /// the description's requiredness is unreliable (D17), and this very model's siblings are typed
    /// nullable in the map precisely because the service omits fields the description marks
    /// required. The check existed only because a required reference type needs the <c>required</c>
    /// modifier to satisfy CS8618, and the modifier needs a value to construct with.
    /// </remarks>
    [Fact]
    public async Task ReadsARowMissingARequiredValueTypedPropertyAsItsDefault()
    {
        Trade trade = Assert.Single(await ReadAsync("""{"id":"t1"}"""));

        Assert.Equal(0m, trade.DecimalSize);
        Assert.Equal(0d, trade.Price);
    }

    /// <summary>A null in a non-nullable value type is a malformed row, not a zero.</summary>
    [Fact]
    public async Task RejectsANullInANonNullableValueType() =>
        await RejectsAsync("""{"id":"t1","decimal_size":"2.0","price":null}""");

    /// <summary>
    /// Numbers are not read from strings and strings are not read from numbers: the default
    /// <c>JsonNumberHandling</c> is strict, and nothing in the SDK relaxes it.
    /// </summary>
    [Theory]
    [InlineData("""{"id":"t1","decimal_size":"2.0","price":"170.15"}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","tape":"3"}""")]
    [InlineData("""{"id":1,"decimal_size":"2.0"}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","conditions":14}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","conditions":[14,"41"]}""")]
    [InlineData("""{"id":"t1","decimal_size":"2.0","price":{"v":1}}""")]
    public async Task RejectsAValueOfTheWrongKind(string row) => await RejectsAsync(row);

    /// <summary>A truncated body fails rather than yielding the rows that arrived before the cut.</summary>
    [Fact]
    public async Task RejectsATruncatedRow() =>
        await RejectsAsync("""{"id":"t1","decimal_size":"2.0","tape":""");

    /// <summary>
    /// A JSON null in a required, non-nullable reference property. System.Text.Json treats
    /// <c>required</c> as a presence check, not a null check, so the key being there satisfies it
    /// and the null lands in a property whose type says it cannot be null. Pinned because the
    /// generated converter must reproduce it rather than improve on it: tightening this would
    /// reject a body the SDK accepts today, which is a behaviour change #47 did not ask for and
    /// nobody would find in a release note about allocation.
    /// </summary>
    [Fact]
    public async Task AcceptsANullInARequiredStringExactlyAsSystemTextJsonDoes()
    {
        Trade trade = Assert.Single(await ReadAsync("""{"id":null,"decimal_size":"2.0"}"""));

        Assert.Null(trade.TradeId);
    }

    /// <summary>
    /// The generated <c>Write</c> round-trips through the generated <c>Read</c>.
    /// </summary>
    /// <remarks>
    /// Nothing in the SDK serializes a model, which is exactly why this is here: the write half of
    /// every generated converter would otherwise have no executable coverage at all, only the
    /// generator tests asserting the text it emits. A consumer caching a page or returning one from
    /// their own endpoint is doing something that works today, and this is what keeps it working.
    /// Driven through a plain <see cref="JsonSerializerOptions"/> rather than the SDK's own context,
    /// because that is what such a consumer would have.
    /// </remarks>
    [Fact]
    public async Task RoundTripsThroughItsOwnWriter()
    {
        Trade original = Assert.Single(await ReadAsync(CompleteRow));

        string written = JsonSerializer.Serialize(original, RoundTrip);
        Trade returned = JsonSerializer.Deserialize<Trade>(written, RoundTrip);

        // Compared as text rather than with Assert.Equal(original, returned): Trade is a record
        // struct, so its synthesized equality compares the Conditions array by reference, and two
        // arrays holding the same codes are never equal. Re-writing the returned value asks the
        // question actually worth asking -- whether anything was lost -- and the array's contents
        // are asserted separately below.
        Assert.Equal(written, JsonSerializer.Serialize(returned, RoundTrip));
        Assert.Equal<int>([14, 41], returned.Conditions!);
        Assert.Equal(original.TradeId, returned.TradeId);
        Assert.Equal(original.SipTimestampNanoseconds, returned.SipTimestampNanoseconds);
        Assert.Equal(original.Price, returned.Price);
    }

    /// <summary>A null array and a null nullable survive the round trip as nulls, not as absences.</summary>
    [Fact]
    public async Task RoundTripsTheNullsToo()
    {
        Trade original = Assert.Single(await ReadAsync("""{"id":"t1","decimal_size":"2.0"}"""));

        string written = JsonSerializer.Serialize(original, RoundTrip);
        Trade returned = JsonSerializer.Deserialize<Trade>(written, RoundTrip);

        // An absent optional is written as an explicit null rather than omitted, which is what
        // System.Text.Json does by default and therefore what a consumer already sees.
        Assert.Contains("\"conditions\":null", written, StringComparison.Ordinal);
        Assert.Null(returned.Conditions);
        Assert.Null(returned.TrfTimestampNanoseconds);

        // Equality is safe here where it is not above: both arrays are null.
        Assert.Equal(original, returned);
    }
}
