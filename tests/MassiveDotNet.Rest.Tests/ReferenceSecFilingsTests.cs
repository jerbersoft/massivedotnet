using System.Net;
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The SEC v1 surface: the dotted parameter names render verbatim, the compact dates D-R9 binds
/// as strings pass through unchanged, the captured fixtures round-trip through
/// <see cref="FilingEntity"/> and <see cref="FilingCompany"/>, filings traverse two stub pages,
/// and the filing file route, generated as the description declares it, throws on the document
/// the service actually serves (D-R4).
/// </summary>
public sealed class ReferenceSecFilingsTests
{
    private const string FilingId = "0001683168-26-006873";
    private const string FileId = "myx_i10k-053126.htm";
    private const string FileUri = "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm";

    /// <summary>The opening of what the filing file route served on 2026-09-03: the document, not the declared object.</summary>
    private const string Html = "<?xml version='1.0' encoding='ASCII'?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>Item 1A. Risk Factors</body></html>";

    /// <summary>The object the description declares at the filing file route, written from the first row of the files capture.</summary>
    private const string DeclaredFile = """
        {
          "description": "FORM 10-K FOR MAY 2026",
          "filename": "myx_i10k-053126.htm",
          "id": "myx_i10k-053126.htm",
          "sequence": 1,
          "size_bytes": 377038,
          "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_i10k-053126.htm",
          "type": "10-K"
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task FilingsRenderEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingsAsync(
                type: "10-K",
                filingDate: RangeFilter.Between("20260101", "20261231"),
                periodOfReportDate: RangeFilter.Gte("20250630"),
                hasXbrl: true,
                companyName: "Apple",
                companyCik: "0000320193",
                companyTicker: "AAPL",
                companySic: "3571",
                companyNameSearch: "Appl",
                order: SortOrder.Descending,
                limit: 2,
                sort: "filing_date",
                cancellationToken: Ct);
        }

        // The compact dates render as given: the route reads yyyyMMdd, and a LocalDate would have
        // rendered yyyy-MM-dd, which the server accepts and misreads (D-R9). The company filters
        // keep the description's dotted wire names.
        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings"
                + "?type=10-K"
                + "&filing_date.gte=20260101&filing_date.lte=20261231"
                + "&period_of_report_date.gte=20250630"
                + "&has_xbrl=true"
                + "&entities.company_data.name=Apple"
                + "&entities.company_data.cik=0000320193"
                + "&entities.company_data.ticker=AAPL"
                + "&entities.company_data.sic=3571"
                + "&entities.company_data.name.search=Appl"
                + "&order=desc&limit=2&sort=filing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FilingsDeserializeTheCapturedPageThroughTheEntities()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingsAsync(type: "10-K", limit: 2, cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("18a640368b180ab2bc59c32d803c411c", page.RequestId);

        Filing filing = page.Results[0];
        Assert.Equal(FilingId, filing.Id);
        Assert.Equal(FilingId, filing.AccessionNumber);
        Assert.Equal("10-K", filing.Type);
        Assert.Equal("20260902", filing.FilingDate);
        Assert.Equal("20260531", filing.PeriodOfReportDate);
        Assert.Equal("20260902103943", filing.AcceptanceTimestamp);
        Assert.Equal(65, filing.FilesCount);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/0001683168-26-006873.txt", filing.SourceUrl);

        FilingEntity entity = Assert.Single(filing.Entities);
        Assert.Equal("filer", entity.Relation);
        Assert.NotNull(entity.CompanyData);
        Assert.Equal("0002087656", entity.CompanyData.Cik);
        Assert.Equal("MYX Inc.", entity.CompanyData.Name);
        Assert.Equal("7374", entity.CompanyData.Sic);
        Assert.Null(entity.CompanyData.Ticker);

        Assert.Equal("PROV", Assert.Single(page.Results[1].Entities).CompanyData?.Ticker);
    }

    [Fact]
    public async Task EnumerateFilingsTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceSecFilings, Fixtures.ReferenceSecFilingsLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Filing filing in client.Reference.EnumerateFilingsAsync(type: "10-K", limit: 2, cancellationToken: Ct))
            {
                ids.Add(filing.Id);
            }
        }

        Assert.Equal(["0001683168-26-006873", "0001010470-26-000010", "0000858877-26-000132"], ids);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.massive.com/v1/reference/sec/filings?type=10-K&limit=2", handler.Requests[0].ToString());
        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings?cursor=YXA9MjAyNjA5MDImYXM9MDAwMTAxMDQ3MC0yNi0wMDAwMTAmbGltaXQ9MiZvcmRlcj1kZXNjJnNvcnQ9ZmlsaW5nX2RhdGUmdHlwZT0xMC1L",
            handler.Requests[1].ToString());
    }

    [Fact]
    public async Task GetFilingBuildsThePathAndDeserializesTheObject()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFiling);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Filing filing;

        using (client)
        using (transport)
        {
            filing = await client.Reference.GetFilingAsync(FilingId, Ct);
        }

        Assert.Equal("https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873", handler.LastRequestUri?.ToString());
        Assert.Equal(FilingId, filing.Id);
        Assert.Equal("20260902", filing.FilingDate);
        Assert.Equal("MYX Inc.", Assert.Single(filing.Entities).CompanyData?.Name);
    }

    [Fact]
    public async Task FilingFilesRenderTheSequenceAndFilenameRanges()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilingFiles);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingFilesAsync(
                FilingId,
                sequence: RangeFilter.Between(1L, 10L),
                filename: RangeFilter.Gte("a"),
                order: SortOrder.Ascending,
                limit: 2,
                sort: "sequence",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files"
                + "?sequence.gte=1&sequence.lte=10&filename.gte=a&order=asc&limit=2&sort=sequence",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FilingFilesDeserializeTheCapturedPage()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilingFiles);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FilingFile> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingFilesAsync(FilingId, limit: 2, cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        FilingFile file = page.Results[0];
        Assert.Equal(FileId, file.Id);
        Assert.Equal(FileId, file.Filename);
        Assert.Equal(1, file.Sequence);
        Assert.Equal(377038, file.SizeBytes);
        Assert.Equal("10-K", file.Type);
        Assert.Equal("FORM 10-K FOR MAY 2026", file.Description);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_i10k-053126.htm", file.SourceUrl);

        Assert.Equal("EX-32.1", page.Results[1].Type);
    }

    [Fact]
    public async Task GetFilingFileDeserializesTheDeclaredObject()
    {
        // The description declares a JSON metadata object as the body (D21); this is the path
        // the generated method takes when the service ever sends it.
        StubHandler handler = new(DeclaredFile);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        FilingFile file;

        using (client)
        using (transport)
        {
            file = await client.Reference.GetFilingFileAsync(FilingId, FileId, Ct);
        }

        Assert.Equal(FileUri, handler.LastRequestUri?.ToString());
        Assert.Equal(FileId, file.Id);
        Assert.Equal(377038, file.SizeBytes);
    }

    [Fact]
    public async Task GetFilingFileThrowsOnTheDocumentTheServiceServes()
    {
        // The service serves the file itself as text/html at this route (2026-09-03). The method
        // ships as declared (D21, D-R4), so the document fails deserialization and surfaces as
        // the API exception every unreadable body does, with the parser's failure inside it.
        StubHandler handler = new(Html) { MediaType = "text/html" };
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassiveApiException exception;

        using (client)
        using (transport)
        {
            exception = await Assert.ThrowsAsync<MassiveApiException>(() => client.Reference.GetFilingFileAsync(FilingId, FileId, Ct));
        }

        Assert.Equal(FileUri, handler.LastRequestUri?.ToString());
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }
}
