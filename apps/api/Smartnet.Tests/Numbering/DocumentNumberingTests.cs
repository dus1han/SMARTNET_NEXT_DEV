using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Numbering;

/// <summary>
/// The promise: numbering continues from the last number the legacy app used, and a duplicate
/// becomes impossible. ISSUES B4 — two different quotations in the live data are both numbered
/// STQ-0, so this is a bug that has already happened, not one that might.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class DocumentNumberingTests
{
    private readonly AuditFixture _fixture;
    private static readonly DateOnly July = new(2026, 7, 14);

    public DocumentNumberingTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Numbering_continues_from_the_last_number_used()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var series = await GivenSeries(db, "STI-", next: 1215);

        var allocator = new DocumentNumberAllocator(db);

        await using var transaction = await db.Database.BeginTransactionAsync();

        var number = await allocator.AllocateAsync(series.CompanyId, series.DocType, July);

        // The legacy app's last invoice was STI-1214. The new app's first must be STI-1215 — not
        // STI-1, which would collide with an invoice issued in 2023.
        number.Should().Be("STI-1215");

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Consecutive_allocations_do_not_repeat_a_number()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var series = await GivenSeries(db, "SNI-", next: 1571);

        var allocator = new DocumentNumberAllocator(db);

        var issued = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            issued.Add(await allocator.AllocateAsync(series.CompanyId, series.DocType, July));
            await transaction.CommitAsync();
        }

        issued.Should().Equal("SNI-1571", "SNI-1572", "SNI-1573");
    }

    [Fact]
    public async Task A_date_prefix_rolls_over_but_the_counter_does_not_reset()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var series = await GivenSeries(db, "{YY}{MON}_SNIN_", next: 1571);

        var allocator = new DocumentNumberAllocator(db);

        await using var july = await db.Database.BeginTransactionAsync();
        var inJuly = await allocator.AllocateAsync(series.CompanyId, series.DocType, July);
        await july.CommitAsync();

        await using var august = await db.Database.BeginTransactionAsync();
        var inAugust = await allocator.AllocateAsync(
            series.CompanyId, series.DocType, new DateOnly(2026, 8, 3));
        await august.CommitAsync();

        inJuly.Should().Be("26JUL_SNIN_1571");

        // The prefix rolls; the number keeps climbing. This is exactly what the live data does —
        // SNI-1556 was followed by 26JUL_SNIN_1562, straight through the rename. Resetting the
        // counter at the rollover would reissue numbers already printed on invoices.
        inAugust.Should().Be("26AUG_SNIN_1572");
    }

    [Fact]
    public async Task A_back_dated_document_gets_the_prefix_of_its_own_date()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var series = await GivenSeries(db, "{YY}{MON}_SNIN_", next: 1571);

        var allocator = new DocumentNumberAllocator(db);

        await using var transaction = await db.Database.BeginTransactionAsync();

        var number = await allocator.AllocateAsync(
            series.CompanyId, series.DocType, new DateOnly(2026, 6, 30));

        // Not today's prefix. A June invoice numbered 26JUL_… contradicts the date printed
        // beside it, and an auditor will ask why.
        number.Should().Be("26JUN_SNIN_1571");

        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Allocating_without_a_transaction_is_refused()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var series = await GivenSeries(db, "STI-", next: 1215);

        var allocator = new DocumentNumberAllocator(db);

        // The row lock lives only as long as the transaction. Allocating outside one reserves
        // nothing: the lock is released before the document is written, and the next caller is
        // handed the same number. Failing loudly is the only safe answer.
        await allocator.Invoking(a => a.AllocateAsync(series.CompanyId, series.DocType, July))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inside the transaction*");
    }

    [Fact]
    public async Task Allocating_with_no_series_configured_fails_loudly()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());
        var allocator = new DocumentNumberAllocator(db);

        await using var transaction = await db.Database.BeginTransactionAsync();

        // The alternative — inventing a series that starts at 1 — would quietly reissue numbers
        // already printed on 2,485 invoices. An error at cutover is cheap; a duplicate invoice
        // number discovered by an auditor is not.
        await allocator
            .Invoking(a => a.AllocateAsync(9999, DocumentTypes.Invoice, July))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No numbering series is configured*");
    }

    [Fact]
    public void The_observed_legacy_prefix_is_recognised_as_a_date_template()
    {
        // The eight invoices prefixed 26JUL_SNIN_ are all dated July 2026, so the "26JUL" in them
        // is evidently the date rather than part of the name. Freezing it as a literal would still
        // be stamping JUL on invoices in August.
        var template = DocumentNumberFormat.Templatise("26JUL_SNIN_", July);

        template.Should().Be("{YY}{MON}_SNIN_");

        DocumentNumberFormat.Render(template, 1571, 0, July).Should().Be("26JUL_SNIN_1571");
        DocumentNumberFormat.Render(template, 1572, 0, new DateOnly(2026, 8, 1))
            .Should().Be("26AUG_SNIN_1572");
    }

    [Fact]
    public void A_prefix_with_no_date_in_it_is_left_alone()
    {
        // STI- must not be mangled into a template. Company 1 has used it throughout, and it
        // contains nothing resembling the date.
        DocumentNumberFormat.Templatise("STI-", July).Should().Be("STI-");
        DocumentNumberFormat.HasTokens("STI-").Should().BeFalse();
    }

    [Fact]
    public async Task Initialising_never_moves_a_counter_backwards()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        // The new app has already issued documents up to 2000 — well past anything in the legacy
        // tables. Re-running the initialiser at that point must not wind the counter back to the
        // legacy maximum and reissue every number in between.
        var series = await GivenSeries(db, "STI-", next: 2000);

        var initialiser = new NumberSeriesInitialiser(db, TimeProvider.System);

        await initialiser.InitialiseAsync(apply: true);

        await using var fresh = _fixture.CreateContext(new FakeChangeContext());
        var reloaded = await fresh.DocumentSeries.SingleAsync(s => s.Id == series.Id);

        reloaded.NextNumber.Should().Be(2000);
    }

    /// <summary>
    /// A real company per test, so that the unique index on (company_id, doc_type) does not make
    /// these tests depend on each other's leftovers — and so that the initialiser, which walks the
    /// companies table, actually sees this series rather than skipping it and passing vacuously.
    /// </summary>
    private static async Task<DocumentSeries> GivenSeries(TestDbContext db, string prefix, long next)
    {
        var company = new Company { Name = $"Test company {prefix}{next}" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var series = new DocumentSeries
        {
            CompanyId = company.Id,
            DocType = DocumentTypes.Invoice,
            Prefix = prefix,
            NextNumber = next,
            Padding = 0,
        };

        db.DocumentSeries.Add(series);
        await db.SaveChangesAsync();

        return series;
    }
}
