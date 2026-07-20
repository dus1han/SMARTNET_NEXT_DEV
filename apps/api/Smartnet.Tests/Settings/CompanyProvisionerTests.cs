using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Settings;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Settings;

/// <summary>
/// Creating a trading entity.
/// </summary>
/// <remarks>
/// The row is the easy part and not what these test. What matters is that the company can <b>do</b>
/// something afterwards: the two existing companies were provisioned by a migration that cross-joined
/// over the companies present when it ran, so a company added later inherits nothing, and the failures
/// that follow all surface a long way from the cause — a tax rate that cannot be resolved at invoice
/// save, numbering that fails at allocation, an email that cannot be sent and cannot be fixed from the
/// UI because there is no create-template endpoint.
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class CompanyProvisionerTests
{
    private readonly AuditFixture _fixture;

    public CompanyProvisionerTests(AuditFixture fixture) => _fixture = fixture;

    private static NewCompany Request(string name, bool vatRegistered = true) => new(
        Name: name,
        IsVatRegistered: vatRegistered,
        VatNumber: vatRegistered ? "VAT-123" : null,
        BusinessRegistrationNo: "BRC-9",
        NumberPrefix: "NEW-",
        TaxRateName: "VAT 18%",
        TaxPercentage: 18m,
        TaxEffectiveFrom: new DateOnly(2026, 1, 1));

    [Fact]
    public async Task A_new_company_is_provisioned_with_everything_it_cannot_work_without()
    {
        var change = new FakeChangeContext { UserId = 1 };
        var name = $"Provisioned {Guid.NewGuid():N}";

        ProvisionedCompany created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await new CompanyProvisioner(db).CreateAsync(Request(name));
        }

        created.TaxRates.Should().Be(2);        // the default, and a zero rate to pick per line
        created.NumberSeries.Should().Be(9);    // one per DocumentTypes.All
        created.EmailTemplates.Should().Be(5);

        await using (var db = _fixture.CreateContext(change))
        {
            var series = await db.DocumentSeries.Where(s => s.CompanyId == created.Id).ToListAsync();
            series.Select(s => s.DocType).Should().BeEquivalentTo(DocumentTypes.All);
            // Starting at 1 is right here and would have been wrong for the legacy companies, which
            // already had 2,500 invoices — hence the migration deliberately seeding no series at all.
            series.Should().OnlyContain(s => s.NextNumber == 1 && s.Prefix == "NEW-");

            var templates = await db.EmailTemplates.Where(t => t.CompanyId == created.Id).ToListAsync();
            templates.Select(t => t.TemplateKey).Should().BeEquivalentTo(EmailTemplateKeys.All);
            templates.Should().OnlyContain(t => t.Subject != "" && t.Body != "");
        }
    }

    [Fact]
    public async Task The_new_company_can_actually_be_taxed_which_is_the_point()
    {
        var change = new FakeChangeContext { UserId = 1 };

        ProvisionedCompany created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await new CompanyProvisioner(db).CreateAsync(Request($"Taxable {Guid.NewGuid():N}"));
        }

        await using (var check = _fixture.CreateContext(change))
        {
            var rates = await check.TaxRates.Where(t => t.CompanyId == created.Id).ToListAsync();

            // The assertion that matters: run the real engine over this company's rates and see that it
            // resolves. Without a default in force it throws TaxRateNotResolvableException, and the
            // company cannot raise an invoice, a quotation or a credit note at all.
            var result = new TaxEngine().Calculate(new TaxCalculationRequest(
                new DateOnly(2026, 7, 20),
                IsVatRegistered: true,
                TaxRounding.PerLine,
                [new TaxLineInput(1m, 100m, 0m)],
                rates,
                0m));

            result.TaxRatePercentage.Should().Be(18m);
            result.Totals.Tax.Should().Be(18m);
        }
    }

    [Fact]
    public async Task A_company_that_is_not_vat_registered_gets_a_zero_default_and_no_vat_rate()
    {
        var change = new FakeChangeContext { UserId = 1 };

        ProvisionedCompany created;
        await using (var db = _fixture.CreateContext(change))
        {
            created = await new CompanyProvisioner(db)
                .CreateAsync(Request($"Unregistered {Guid.NewGuid():N}", vatRegistered: false));
        }

        created.TaxRates.Should().Be(1);

        await using (var db = _fixture.CreateContext(change))
        {
            var rates = await db.TaxRates.Where(t => t.CompanyId == created.Id).ToListAsync();

            // Seeding an 18% rate it can never charge would be a row that lies about the company: the
            // engine forces 0% for an unregistered company whatever the table says.
            rates.Should().ContainSingle();
            rates[0].Percentage.Should().Be(0m);
            rates[0].IsDefault.Should().BeTrue();

            (await db.Companies.FirstAsync(c => c.Id == created.Id)).VatNumber.Should().BeNull();
        }
    }

    [Fact]
    public async Task The_same_name_twice_is_refused()
    {
        var change = new FakeChangeContext { UserId = 1 };
        var name = $"Duplicate {Guid.NewGuid():N}";

        await using (var db = _fixture.CreateContext(change))
        {
            await new CompanyProvisioner(db).CreateAsync(Request(name));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var again = async () => await new CompanyProvisioner(db).CreateAsync(Request(name));

            await again.Should().ThrowAsync<CompanyAlreadyExistsException>();
        }
    }

    [Fact]
    public async Task Nothing_is_left_behind_when_the_name_is_rejected()
    {
        var change = new FakeChangeContext { UserId = 1 };
        var name = $"Atomic {Guid.NewGuid():N}";

        await using (var db = _fixture.CreateContext(change))
        {
            await new CompanyProvisioner(db).CreateAsync(Request(name));
        }

        await using (var db = _fixture.CreateContext(change))
        {
            try
            {
                await new CompanyProvisioner(db).CreateAsync(Request(name));
            }
            catch (CompanyAlreadyExistsException)
            {
                // expected
            }
        }

        await using (var check = _fixture.CreateContext(change))
        {
            // One company, one set of series — a rejected second attempt must not have left a second
            // company's worth of numbering or templates lying around.
            var companies = await check.Companies.Where(c => c.Name == name).ToListAsync();
            companies.Should().ContainSingle();

            var series = await check.DocumentSeries.CountAsync(s => s.CompanyId == companies[0].Id);
            series.Should().Be(9);
        }
    }
}
