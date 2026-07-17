using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Ledger;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Ledger;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Ledger;

/// <summary>
/// The general-ledger posting engine (GL slice 1/2): every money event posts one balanced double-entry,
/// idempotently, resolving/creating accounts by code.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class GeneralLedgerTests
{
    private readonly AuditFixture _fixture;

    public GeneralLedgerTests(AuditFixture fixture) => _fixture = fixture;

    private static GlPosting Invoice(long companyId, long invoiceId) => new(
        companyId, new DateOnly(2026, 7, 17), "Invoice", invoiceId, "INV-1",
        [
            new GlPostingLine(GlAccountCodes.AccountsReceivable, "Accounts Receivable", AccountType.Asset, false, 118m, 0m),
            new GlPostingLine(GlAccountCodes.Sales, "Sales", AccountType.Income, false, 0m, 100m),
            new GlPostingLine(GlAccountCodes.OutputVat, "Output VAT", AccountType.Liability, false, 0m, 18m),
        ]);

    [Fact]
    public async Task A_balanced_event_posts_one_entry_that_resolves_its_accounts()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        await using (var db = _fixture.CreateContext(change))
        {
            (await new GeneralLedger(db).PostAsync(Invoice(companyId, 5001))).Should().BeTrue();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var entry = await db.GlEntries.Include(e => e.Lines).SingleAsync(e => e.SourceType == "Invoice" && e.SourceId == 5001);
            entry.Lines.Should().HaveCount(3);
            entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit)); // balances
            entry.Lines.Sum(l => l.Debit).Should().Be(118m);

            // Its accounts were created for this company.
            (await db.GlAccounts.CountAsync(a => a.CompanyId == companyId && a.Code == GlAccountCodes.AccountsReceivable)).Should().Be(1);
        }
    }

    [Fact]
    public async Task Posting_the_same_event_twice_is_idempotent()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        await using (var db = _fixture.CreateContext(change))
        {
            (await new GeneralLedger(db).PostAsync(Invoice(companyId, 5002))).Should().BeTrue();
        }
        await using (var db = _fixture.CreateContext(change))
        {
            (await new GeneralLedger(db).PostAsync(Invoice(companyId, 5002))).Should().BeFalse(); // already posted
        }

        await using (var db = _fixture.CreateContext(change))
        {
            (await db.GlEntries.CountAsync(e => e.SourceType == "Invoice" && e.SourceId == 5002)).Should().Be(1);
        }
    }

    [Fact]
    public async Task An_unbalanced_posting_is_refused()
    {
        var companyId = await SeedCompany();
        var change = new FakeChangeContext { UserId = 1, CompanyId = companyId };

        await using var db = _fixture.CreateContext(change);
        var unbalanced = new GlPosting(companyId, new DateOnly(2026, 7, 17), "Invoice", 5003, null,
            [
                new GlPostingLine(GlAccountCodes.AccountsReceivable, "Accounts Receivable", AccountType.Asset, false, 100m, 0m),
                new GlPostingLine(GlAccountCodes.Sales, "Sales", AccountType.Income, false, 0m, 90m),
            ]);

        var act = () => new GeneralLedger(db).PostAsync(unbalanced);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task<long> SeedCompany()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });
        var company = new Company { Name = "Smart Net (test)", VatCode = "1", IsVatRegistered = true };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }
}
