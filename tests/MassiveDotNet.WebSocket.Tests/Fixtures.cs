namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Response bodies taken verbatim from Massive's published streaming samples, so deserialization
/// is exercised against the shape the docs actually declare. The stock market was closed the day
/// these were written, so no live capture was possible (Task 14 confirms the wire shapes during
/// market hours).
/// </summary>
internal static class Fixtures
{
    /// <summary>
    /// The published sample for a stock trade event, the <c>T</c> topic, wrapped in the array the
    /// wire actually delivers. It omits <c>ds</c>, <c>trfi</c>, and <c>trft</c>, which is the
    /// documentation's own evidence that they are optional.
    /// </summary>
    public const string StockTrade = """
        [{"ev":"T","sym":"MSFT","x":4,"i":"12345","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":3681328}]
        """;

    /// <summary>
    /// The published sample for a stock NBBO quote event, the <c>Q</c> topic, wrapped in the array
    /// the wire actually delivers.
    /// </summary>
    public const string StockQuote = """
        [{"ev":"Q","sym":"MSFT","bx":4,"bp":114.125,"bs":100,"ax":7,"ap":114.128,"as":160,"c":0,"i":[604],"t":1536036818784,"q":50385480,"z":3}]
        """;

    /// <summary>
    /// The published sample for a second aggregate, the <c>A</c> topic, wrapped in the array the
    /// wire actually delivers. It omits <c>dv</c>, <c>dav</c> and <c>otc</c>, which is the
    /// documentation's own evidence that they are optional — see <see cref="StockSecondAggregateLive"/>
    /// for a frame that carries the first two.
    /// </summary>
    public const string StockSecondAggregate = """
        [{"ev":"A","sym":"SPCE","v":200,"av":8642007,"op":25.66,"vw":25.3981,"o":25.39,"c":25.39,"h":25.39,"l":25.39,"a":25.3714,"z":50,"s":1610144868000,"e":1610144869000}]
        """;

    /// <summary>
    /// The published sample for a minute aggregate, the <c>AM</c> topic. Field-for-field identical
    /// to the second aggregate above, which is why one model serves both (D-W14).
    /// </summary>
    public const string StockMinuteAggregate = """
        [{"ev":"AM","sym":"GTE","v":4110,"av":9470157,"op":0.4372,"vw":0.4488,"o":0.4488,"c":0.4486,"h":0.4489,"l":0.4486,"a":0.4352,"z":685,"s":1610144640000,"e":1610144700000}]
        """;

    /// <summary>
    /// A second aggregate captured live from <c>wss://socket.massive.com/stocks</c> on 2026-09-08
    /// at 09:52 ET. Committed because it carries <c>dv</c> and <c>dav</c>, which the published
    /// sample omits and no fixture drawn from that sample could exercise. Reviewed before
    /// committing: it carries no account identifier and no URL.
    /// </summary>
    public const string StockSecondAggregateLive = """
        [{"ev":"A","sym":"FCX","v":4989,"av":4332125,"op":75.98,"vw":78.0894,"o":78.1,"c":78.08,"h":78.125,"l":78.07,"a":76.8404,"z":62,"s":1788877043000,"e":1788877044000,"dv":"4989.0","dav":"4332125.038360"}]
        """;
}
