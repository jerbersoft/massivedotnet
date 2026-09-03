using System.Text;
using System.Text.Json;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The SEC v1 surface against the real service: a page boundary at a small limit, the compact
/// date filter D-R9 exists for, and the two halves of the filing file route — the declared method
/// throwing on the document the service sends, and the download copying it (D-R4, D-R13).
/// </summary>
public sealed class ReferenceSecFilingsLiveTests : LiveApiTest
{
    // The date the compact filter is exercised from. Fixed rather than computed from a clock so
    // the window cannot drift under the assertion.
    private const string WindowStart = "20260101";

    [Fact]
    public async Task FilingsCrossAPageBoundary()
    {
        // limit is per page, so five filings at two per page is three requests. A repeated or
        // skipped accession number across the seam is what an incorrectly rebuilt cursor looks
        // like; the SEC cursor carries its position in the query string (D14).
        List<string> ids = [];

        await foreach (Filing filing in Client.Reference.EnumerateFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 2,
            cancellationToken: Ct))
        {
            ids.Add(filing.Id);

            if (ids.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheCompactDateFilterSelectsOnOrAfterTheDateGiven()
    {
        // The route reads yyyyMMdd. This is the assertion D-R9 rests on: the filter the SDK can
        // express selects the window it names. A LocalDate would have rendered 2026-01-01 here,
        // which this route accepts and does not read as this date.
        MassivePage<Filing> page = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            filingDate: RangeFilter.Gte(WindowStart),
            order: SortOrder.Ascending,
            sort: "filing_date",
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Filing filing in page.Results)
        {
            // Compact dates sort lexically, so an ordinal comparison is a date comparison.
            Assert.True(
                string.CompareOrdinal(filing.FilingDate, WindowStart) >= 0,
                $"Expected a filing on or after {WindowStart}; got {filing.FilingDate}.");

            Assert.Equal("10-K", filing.Type);
            Assert.Equal(8, filing.FilingDate.Length);
            Assert.NotEmpty(filing.Entities);
        }
    }

    [Fact]
    public async Task AFilingFromTheListRoundTripsThroughTheGet()
    {
        // The identifier comes from the list rather than being hard-coded: a filing is a permanent
        // record, but which one is newest is not, and the get is what proves the identifier the
        // list reports is the one the get accepts.
        MassivePage<Filing> page = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        Filing listed = Assert.Single(page.Results);

        Filing fetched = await Client.Reference.GetFilingAsync(listed.Id, Ct);

        Assert.Equal(listed.Id, fetched.Id);
        Assert.Equal(listed.AccessionNumber, fetched.AccessionNumber);
        Assert.Equal(listed.FilingDate, fetched.FilingDate);
        Assert.Equal(listed.FilesCount, fetched.FilesCount);
        Assert.Equal(14, fetched.AcceptanceTimestamp?.Length);

        FilingEntity entity = Assert.Single(fetched.Entities);
        Assert.Equal("filer", entity.Relation);
        Assert.NotNull(entity.CompanyData);
        Assert.False(string.IsNullOrEmpty(entity.CompanyData.Name));
        Assert.False(string.IsNullOrEmpty(entity.CompanyData.Cik));
    }

    [Fact]
    public async Task TheFilingFileRouteServesTheDocumentBothWays()
    {
        // One test, because both halves need the same file and a second lookup would cost another
        // two calls. The declared method throws on the HTML the service sends, which is D21
        // applied to a route that drifted rather than retired; the download is the method that
        // works, and the pin flips the day the service serves the declared object (D-R4, D25).
        MassivePage<Filing> filings = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        string filingId = Assert.Single(filings.Results).Id;

        MassivePage<FilingFile> files = await Client.Reference.ListFilingFilesAsync(
            filingId,
            order: SortOrder.Ascending,
            sort: "sequence",
            limit: 1,
            cancellationToken: Ct);

        FilingFile file = Assert.Single(files.Results);
        Assert.Equal(1, file.Sequence);
        Assert.True(file.SizeBytes > 0);
        Assert.False(string.IsNullOrEmpty(file.Type));
        Assert.False(string.IsNullOrEmpty(file.Description));

        // Observed 2026-09-03: text/html, so the declared FilingFile cannot be read from it.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.GetFilingFileAsync(filingId, file.Id, Ct));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);

        using MemoryStream destination = new();
        await Client.Reference.DownloadFilingFileAsync(filingId, file.Id, destination, Ct);

        Assert.Equal(file.SizeBytes, destination.Length);
        Assert.StartsWith("<", Encoding.UTF8.GetString(destination.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilingFilesEnumerateBeyondTheFirstPage()
    {
        MassivePage<Filing> filings = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        string filingId = Assert.Single(filings.Results).Id;
        List<long> sequences = [];

        await foreach (FilingFile file in Client.Reference.EnumerateFilingFilesAsync(
            filingId,
            order: SortOrder.Ascending,
            sort: "sequence",
            limit: 2,
            cancellationToken: Ct))
        {
            sequences.Add(file.Sequence);

            if (sequences.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, sequences.Count);
        Assert.Equal(sequences.Order(), sequences);
    }
}
