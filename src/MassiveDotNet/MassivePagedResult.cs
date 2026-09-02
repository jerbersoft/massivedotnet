namespace MassiveDotNet;

/// <summary>
/// One page of a paginated Massive endpoint whose page is a single object rather than an array,
/// such as a technical indicator whose values continue across pages while each page carries its
/// own underlying aggregates.
/// </summary>
/// <typeparam name="T">The result object type.</typeparam>
/// <remarks>
/// <para>
/// The sibling of <see cref="MassivePage{T}"/> with <typeparamref name="T"/> in place of
/// <c>T[]</c>. Returned by the <c>List</c> methods of such endpoints; the matching <c>Enumerate</c>
/// method walks every page and yields the items inside each result object as one flat sequence,
/// which is why only <c>List</c> can show the rest of the object.
/// </para>
/// <para>
/// The cursor itself is deliberately not exposed, for the reason <see cref="MassivePage{T}"/>
/// gives: there is no public API that accepts one back.
/// </para>
/// <para>
/// Equality is the compiler-synthesized record equality, which compares <see cref="Result"/> with
/// its own equality. A <see langword="default"/> instance, which any struct permits, has a
/// <see langword="null"/> <see cref="Result"/>; the SDK never constructs one, and the constructor
/// refuses a null result so that the only way to obtain one is to ask for <see langword="default"/>.
/// </para>
/// </remarks>
public readonly record struct MassivePagedResult<T>
{
    /// <summary>Initializes a new instance of the <see cref="MassivePagedResult{T}"/> struct.</summary>
    /// <param name="result">The page's result object.</param>
    /// <param name="hasMore">Whether the server offered a cursor to a further page.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public MassivePagedResult(T result, bool hasMore, string? requestId)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        HasMore = hasMore;
        RequestId = requestId;
    }

    /// <summary>The result object in this page. Never <see langword="null"/> when constructed through the SDK.</summary>
    public T Result { get; }

    /// <summary>
    /// Whether more pages exist. When <see langword="true"/>, the matching <c>Enumerate</c> method
    /// will retrieve the remainder.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// The server-assigned request identifier. Include this when contacting Massive support.
    /// <see langword="null"/> when the endpoint does not return one.
    /// </summary>
    public string? RequestId { get; }
}
