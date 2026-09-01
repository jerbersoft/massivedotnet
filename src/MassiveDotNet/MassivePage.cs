namespace MassiveDotNet;

/// <summary>
/// One page of results from a paginated Massive endpoint.
/// </summary>
/// <typeparam name="T">The result item type.</typeparam>
/// <remarks>
/// <para>
/// Returned by the <c>List</c> methods of endpoints that paginate. To walk every page instead of
/// handling cursors yourself, use the matching <c>Enumerate</c> method, which returns an
/// <see cref="IAsyncEnumerable{T}"/>.
/// </para>
/// <para>
/// The cursor itself is deliberately not exposed. It is a server-supplied absolute URL that the
/// SDK validates and follows internally; there is no public API that accepts one back, so
/// surfacing it would offer callers a value they could not use.
/// </para>
/// <para>
/// Equality is the compiler-synthesized record equality, which compares <see cref="Results"/> by
/// reference rather than by content: two pages holding equal but distinct arrays are not equal,
/// and a page constructed from <see langword="null"/> is not equal to one constructed from an
/// empty array even though both expose an empty <see cref="Results"/>. That is unavoidable for a
/// record holding an array; compare <see cref="Results"/> directly when the contents are what you
/// mean to compare.
/// </para>
/// </remarks>
public readonly record struct MassivePage<T>
{
    private readonly T[]? _results;

    /// <summary>Initializes a new instance of the <see cref="MassivePage{T}"/> struct.</summary>
    /// <param name="results">The page's results, or <see langword="null"/> when the server sent none.</param>
    /// <param name="hasMore">Whether the server offered a cursor to a further page.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    public MassivePage(T[]? results, bool hasMore, string? requestId)
    {
        _results = results;
        HasMore = hasMore;
        RequestId = requestId;
    }

    /// <summary>
    /// The results in this page. Never <see langword="null"/>; empty when the server returned none.
    /// </summary>
    public T[] Results => _results ?? [];

    /// <summary>
    /// Whether more pages exist. When <see langword="true"/>, the matching <c>Enumerate</c> method
    /// will retrieve the remainder.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// The server-assigned request identifier. Include this when contacting Massive support.
    /// <see langword="null"/> when the endpoint does not return one: a few paginated envelopes
    /// declare no <c>request_id</c> at all, so there is nothing to surface for them.
    /// </summary>
    public string? RequestId { get; }
}
