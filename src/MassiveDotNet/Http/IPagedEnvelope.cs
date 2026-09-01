namespace MassiveDotNet.Http;

/// <summary>
/// A response envelope that carries one page of results and, when more exist, a cursor to the
/// next page.
/// </summary>
/// <typeparam name="TItem">The result item type.</typeparam>
/// <remarks>
/// Implemented by generated response envelopes for the operations whose OpenAPI success schema
/// declares <c>next_url</c>. It exists so that
/// <see cref="MassiveHttpTransport.EnumerateAsync{TEnvelope, TItem}"/> can traverse any of them
/// with a single implementation, rather than the generator emitting one traversal loop per
/// endpoint.
/// </remarks>
public interface IPagedEnvelope<TItem>
{
    /// <summary>The results in this page, or <see langword="null"/> when the server sent none.</summary>
    TItem[]? Results { get; }

    /// <summary>The absolute URL of the next page, or <see langword="null"/> on the final page.</summary>
    string? NextUrl { get; }
}
