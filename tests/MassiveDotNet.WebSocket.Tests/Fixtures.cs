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
}
