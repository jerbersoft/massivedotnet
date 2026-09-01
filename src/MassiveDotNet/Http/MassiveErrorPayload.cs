using System.Text.Json.Serialization;

namespace MassiveDotNet.Http;

/// <summary>
/// A best-effort view of a Massive error body. The OpenAPI description does not schematize
/// error responses, so every member is optional and the transport falls back to the HTTP
/// reason phrase when nothing usable is present.
/// </summary>
internal sealed class MassiveErrorPayload
{
    /// <summary>The error text, on endpoints that use an <c>error</c> field.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>The error text, on endpoints that use a <c>message</c> field.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The response status, typically <c>"ERROR"</c> or <c>"NOT_AUTHORIZED"</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>The server-assigned request identifier.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>Returns the most descriptive message available, or <see langword="null"/>.</summary>
    /// <returns>The error text, preferring <see cref="Error"/> over <see cref="Message"/> over <see cref="Status"/>.</returns>
    public string? BestMessage() =>
        FirstNonBlank(Error) ?? FirstNonBlank(Message) ?? FirstNonBlank(Status);

    private static string? FirstNonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
