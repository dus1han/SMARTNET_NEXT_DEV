using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Documents;
using Smartnet.Infrastructure.Numbering;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Job cards (Phase 6, slice 3): the lightest document — no tax, ledger or stock — with structured
/// serial-tracked lines (replacing the legacy text blob) and a guarded PENDING → CLOSED workflow.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class JobCardTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    public JobCardTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_job_card_is_booked_pending_with_structured_serial_lines_and_a_dual_written_blob()
    {
        var (companyId, customerId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        JobCardCreated created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await ServiceFor(db, change).CreateAsync(new NewJobCard(
                companyId, customerId, new DateOnly(2026, 7, 16),
                ContactPerson: "Mr Khan", FaultDescription: "Won't power on", Remarks: "Under warranty",
                Technician: "Sam",
                Lines:
                [
                    new NewJobCardLine(null, "Dell Latitude", "SN-AAA"),
                    new NewJobCardLine(null, "Dell Charger", "SN-BBB"),
                ]));
        }

        created.Number.Should().Be($"JOB{companyId}-300");

        await using (var db = _fixture.CreateContext(change))
        {
            var card = await db.JobCards.Include(j => j.Lines).FirstAsync(j => j.Id == created.Id);
            card.Status.Should().Be(JobCardStatus.Pending);
            card.IsClosed.Should().BeFalse();
            card.FaultDescription.Should().Be("Won't power on");
            card.Technician.Should().Be("Sam");
            card.Cost.Should().BeNull();
            card.Lines.Should().HaveCount(2);
            card.Lines.OrderBy(l => l.Sort).Select(l => l.Serial).Should().Equal("SN-AAA", "SN-BBB");

            // The legacy items blob was dual-written for the Crystal job sheet — one line per serial.
            var blob = await db.Database
                .SqlQuery<string>($"SELECT items AS Value FROM jobs_m WHERE id = {created.Id}")
                .SingleAsync();
            blob.Should().Contain("Serial No : SN-AAA").And.Contain("Serial No : SN-BBB");

            // v1 snapshot exists.
            (await db.DocumentVersions.CountAsync(v => v.DocType == DocumentTypes.JobCard && v.DocId == created.Id))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Closing_a_job_records_cost_and_sell_and_a_second_close_is_refused()
    {
        var (companyId, customerId) = await Seed();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        long id;
        int rowVersion;
        await using (var db = _fixture.CreateContext(change))
        {
            id = (await ServiceFor(db, change).CreateAsync(new NewJobCard(
                companyId, customerId, new DateOnly(2026, 7, 16), null, "Fault", null, "Sam",
                [new NewJobCardLine(null, "Laptop", "SN-1")]))).Id;
            rowVersion = await db.JobCards.Where(j => j.Id == id).Select(j => j.RowVersion).SingleAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            await ServiceFor(db, change).CloseAsync(id, new CloseJobCard(120m, 200m, "Replaced the board"), rowVersion);
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var card = await db.JobCards.FirstAsync(j => j.Id == id);
            card.Status.Should().Be(JobCardStatus.Closed);
            card.Cost.Should().Be(120m);
            card.Sell.Should().Be(200m);
            card.CompletionRemarks.Should().Be("Replaced the board");
            card.CompletedBy.Should().Be(1);
            card.CompletedAt.Should().NotBeNull();

            // The legacy close columns were dual-written (cost, and the misspelled dompleteddt).
            var legacyCost = await db.Database
                .SqlQuery<string>($"SELECT cost AS Value FROM jobs_m WHERE id = {id}").SingleAsync();
            legacyCost.Should().Be("120");
            var completedDt = await db.Database
                .SqlQuery<string>($"SELECT dompleteddt AS Value FROM jobs_m WHERE id = {id}").SingleAsync();
            completedDt.Should().NotBeNullOrEmpty();

            // A second snapshot (v2) recorded the close.
            (await db.DocumentVersions.CountAsync(v => v.DocType == DocumentTypes.JobCard && v.DocId == id))
                .Should().Be(2);
        }

        // A second close is refused — the card is no longer PENDING (the legacy re-close hazard).
        await using (var db = _fixture.CreateContext(change))
        {
            var current = await db.JobCards.Where(j => j.Id == id).Select(j => j.RowVersion).SingleAsync();
            var act = () => ServiceFor(db, change).CloseAsync(id, new CloseJobCard(1m, 1m, null), current);
            await act.Should().ThrowAsync<JobCardNotPendingException>();
        }
    }

    // --- Seeding ---------------------------------------------------------------------------------

    private static JobCardService ServiceFor(TestDbContext db, FakeChangeContext change) => new(
        db,
        new DocumentNumberAllocator(db),
        new DocumentVersionWriter(db, change, Clock),
        change,
        Clock);

    private async Task<(long CompanyId, long CustomerId)> Seed()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = false };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var customer = new Customer { Code = $"C-{company.Id}", Name = "Acme" };
        db.Customers.Add(customer);
        db.DocumentSeries.Add(new DocumentSeries
        {
            CompanyId = company.Id, DocType = DocumentTypes.JobCard, Prefix = $"JOB{company.Id}-", NextNumber = 300, Padding = 0,
        });
        await db.SaveChangesAsync();

        return (company.Id, customer.Id);
    }
}
