using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// Reference data across asset classes: tickers, news, corporate actions, exchanges,
/// conditions, SEC filings, and financials. Reached through <see cref="MassiveRestClient.Reference"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="struct"/> wrapping the shared transport, so navigating to a group
/// costs no allocation. Endpoint methods live in the generated half of this partial type. The one
/// hand-written member is <see cref="DownloadFilingFileAsync"/>, which the description cannot
/// generate because it declares JSON where the service serves a document (decision D25).
/// </remarks>
public readonly partial struct ReferenceGroup
{
    private readonly MassiveHttpTransport _transport;

    internal ReferenceGroup(MassiveHttpTransport transport) => _transport = transport;

    /// <summary>
    /// Downloads one file within an SEC filing, copying its content to
    /// <paramref name="destination"/> unchanged.
    /// </summary>
    /// <param name="filingId">The filing's identifier, as <see cref="Models.Filing.Id"/> reports it.</param>
    /// <param name="fileId">The file's identifier, as <see cref="Models.FilingFile.Id"/> reports it.</param>
    /// <param name="destination">The stream the file is written to. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes once the whole file has been written.</returns>
    /// <remarks>
    /// The description declares a JSON metadata object at this route, which
    /// <see cref="GetFilingFileAsync"/> retrieves as declared; the service serves the file itself,
    /// which is what this method is for (decision D25). The bytes are copied as sent, whatever
    /// their type: <see cref="ListFilingFilesAsync"/> names each file's type and size, so the
    /// caller knows what it asked for. The URI comes from the same generated builder the declared
    /// method uses, so the two cannot drift apart. No content type is inspected, so if the service
    /// ever stops drifting and starts serving the declared JSON object at this route, this method
    /// copies that object's own bytes to <paramref name="destination"/> without error; <see
    /// cref="GetFilingFileAsync"/> is what pins the drift and starts succeeding, rather than
    /// throwing, on that day, but this method carries no equivalent signal.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="filingId"/> or <paramref name="fileId"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="MassiveApiException">The server responded with an error status.</exception>
    public Task DownloadFilingFileAsync(
        string filingId,
        string fileId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);

        string requestUri = BuildGetFilingFileUri(filingId, fileId);
        return _transport.DownloadAsync(requestUri, destination, cancellationToken);
    }
}
