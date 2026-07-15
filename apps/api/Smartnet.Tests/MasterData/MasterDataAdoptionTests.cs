using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.MasterData;

/// <summary>
/// The slice-1 exit: the master tables are ours, and the legacy app can still write them.
/// </summary>
/// <remarks>
/// Both halves matter, and the second is the one that gets forgotten. <b>The legacy app is still the
/// live app.</b> It inserts customers, suppliers and items every day, knowing nothing about the
/// primary key, the audit columns or the retyped credit limit this migration added underneath it. If
/// one of its INSERTs starts failing on the morning this deploys, the business stops — and it stops
/// in a way whose cause is a migration nobody will connect it to.
///
/// <para>So these tests write the way the old app writes: raw SQL, naming only the columns it knows,
/// with money as a string. Then they read the row back through the new entity and check it is the
/// same customer.</para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class MasterDataAdoptionTests
{
    private readonly AuditFixture _fixture;

    public MasterDataAdoptionTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_legacy_insert_still_succeeds_and_is_readable_as_a_customer()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        // Exactly what the legacy app does: no id, no audit columns, and the credit limit as a
        // string — because in the old schema `climit` was a varchar(100), and its C# passes a string.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO cus_m (cuscode, cusname, custype, contactp, cusadd, contactno, email,
                               c_form, pro, vatnum, climit)
            VALUES ('C-9001', 'Ceylon Fabrics (Pvt) Ltd', 'Company', 'Nimal', 'Colombo 03',
                    '0112345678', 'accounts@ceylonfabrics.lk', '2', '3', 'VAT-9001', '250000')
            """);

        var customer = await db.Customers.SingleAsync(c => c.Code == "C-9001");

        // The surrogate key the legacy INSERT never mentioned. AUTO_INCREMENT filled it in.
        customer.Id.Should().BeGreaterThan(0);

        customer.Name.Should().Be("Ceylon Fabrics (Pvt) Ltd");

        // Money, as a number, from a string the legacy app wrote. This is Finding 5 closed: the
        // column can now be summed, and can no longer hold "12,500".
        customer.CreditLimit.Should().Be(250_000m);

        // c_form: the trading entity the customer is associated with — an indication, not a boundary.
        customer.AssignedCompanyId.Should().Be(2);
        customer.ProfitPercentId.Should().Be(3);

        // The audit columns defaulted, so the row is valid for the concurrency check even though the
        // legacy app has never heard of row_version.
        customer.RowVersion.Should().Be(1);
        customer.CreatedAt.Should().NotBe(default);
        customer.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task A_legacy_insert_still_succeeds_for_suppliers_and_items()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO sup_m (supcode, supname, contactp, supadd, contactno, email, vatnum)
            VALUES ('S-9001', 'Lanka Cables', 'Sunil', 'Ratmalana', '0119876543', 's@lc.lk', 'VAT-77')
            """);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO item_m (itemcode, itemname) VALUES ('I-9001', '166A COMPATIBLE TONER')
            """);

        (await db.Suppliers.SingleAsync(s => s.Code == "S-9001")).Name.Should().Be("Lanka Cables");

        var item = await db.Items.SingleAsync(i => i.Code == "I-9001");

        item.Id.Should().BeGreaterThan(0);
        item.Name.Should().Be("166A COMPATIBLE TONER");
    }

    [Fact]
    public async Task Stock_money_and_dates_survive_the_retype()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        // The legacy app writes all five of these as strings — cost, quantity, balance, and two
        // dates. Every one of them parsed in production, which is what made the retype safe.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO item_stock (item_code, unitcost, indate, warranty, quantity, balance,
                                    enteredby, enteredat)
            VALUES ('I-9001', '990.50', '2025-09-29', '0', '10000', '10000',
                    'Chanaka Kotugoda', '2025-09-29 11:27:30')
            """);

        var batch = await db.StockBatches.SingleAsync(b => b.ItemCode == "I-9001");

        batch.UnitCost.Should().Be(990.50m);
        batch.Quantity.Should().Be(10_000m);
        batch.InDate.Should().Be(new DateOnly(2025, 9, 29));
        batch.EnteredAt.Should().Be(new DateTime(2025, 9, 29, 11, 27, 30, DateTimeKind.Unspecified));

        // The legacy attribution: a display name, not a user id. Kept because the old app writes it,
        // and superseded by created_by, which the audit spine fills in for anything we write.
        batch.EnteredBy.Should().Be("Chanaka Kotugoda");
    }

    [Fact]
    public async Task A_credit_limit_that_is_not_a_number_is_now_rejected()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        // The whole reason for the retype. In the legacy schema this INSERT succeeds and the
        // customer's credit limit is the string "abc" — which then reaches `Convert.ToDouble` and
        // decides whether an invoice may be raised.
        var junk = () => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO cus_m (cuscode, cusname, climit) VALUES ('C-9002', 'Junk Ltd', 'abc')
            """);

        await junk.Should().ThrowAsync<Exception>("a credit limit that is not a number is not a credit limit");
    }

    [Fact]
    public async Task Two_customers_cannot_share_a_code()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext());

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO cus_m (cuscode, cusname, climit) VALUES ('C-9003', 'First', '0')");

        // Nothing prevented this before — cus_m had no key and no unique index, and "C-1" is the only
        // thing the business identifies a customer by. There are zero duplicates today, which is luck
        // rather than protection. This is the protection.
        var duplicate = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO cus_m (cuscode, cusname, climit) VALUES ('C-9003', 'Second', '0')");

        // The database refuses it — which is the only place a refusal is worth anything. The legacy
        // app would have to remember to check, on every screen that writes a customer, forever.
        (await duplicate.Should().ThrowAsync<Exception>())
            .Which.Message.Should().Contain("Duplicate entry");
    }

    [Fact]
    public async Task A_customer_we_write_is_audited_without_any_audit_code()
    {
        var chanaka = new FakeChangeContext
        {
            UserId = 1,
            CompanyId = 2,
            Reason = "New account opened; credit limit agreed with the director.",
        };

        await using var db = _fixture.CreateContext(chanaka);

        var customer = new Customer
        {
            Code = "C-9004",
            Name = "Nuwara Eliya Traders",
            CreditLimit = 75_000m,
            AssignedCompanyId = 2,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        // The point of adopting the table at all: everything written to it from here on inherits the
        // audit spine, and no endpoint had to remember to ask for it.
        var entry = await db.AuditLog
            .Where(a => a.EntityType == nameof(Customer)
                        && a.EntityId == customer.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .OrderByDescending(a => a.Id)
            .FirstAsync();

        entry.Action.Should().Be(AuditAction.Create);
        entry.ChangedBy.Should().Be(1);
        entry.Reason.Should().Be(chanaka.Reason);
        entry.Changes.Should().Contain("Nuwara Eliya Traders");

        customer.CreatedBy.Should().Be(1);
        customer.RowVersion.Should().Be(1);
    }

    [Fact]
    public async Task Deleting_a_supplier_is_a_soft_delete_that_stays_attributable()
    {
        await using var db = _fixture.CreateContext(new FakeChangeContext { UserId = 1 });

        var supplier = new Supplier { Code = "S-9002", Name = "Gone Ltd" };

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        // The legacy app has no delete for suppliers at all — a supplier you stop buying from stays
        // in the picker forever. Here it disappears from the list...
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync();

        (await db.Suppliers.AnyAsync(s => s.Code == "S-9002")).Should().BeFalse();

        // ...and is still there, saying who removed it. Every purchase order it is on still names it.
        var deleted = await db.Suppliers
            .IgnoreQueryFilters()
            .SingleAsync(s => s.Code == "S-9002");

        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedBy.Should().Be(1);
    }
}
