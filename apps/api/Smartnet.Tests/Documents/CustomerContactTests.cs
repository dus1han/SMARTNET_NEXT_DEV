using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Smartnet.Domain.MasterData;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Documents;

/// <summary>
/// Structured customer contacts (Phase 6, slice 4): the real <c>customer_contacts</c> rows behind the
/// legacy <c>;</c>-separated strings. Verifies the mapping, the parent link and the cascade.
/// </summary>
[Collection(nameof(AuditCollection))]
public sealed class CustomerContactTests
{
    private readonly AuditFixture _fixture;

    public CustomerContactTests(AuditFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Contacts_persist_against_a_customer_and_cascade_when_it_is_removed()
    {
        var change = new FakeChangeContext { UserId = 1 };

        long customerId;
        await using (var db = _fixture.CreateContext(change))
        {
            var customer = new Customer
            {
                Code = "C-CT1",
                Name = "Acme",
                Contacts =
                [
                    new CustomerContact { Name = "Priya", Role = "Accounts", Email = "priya@acme.test", Usage = ContactUsage.DocumentsAndNotifications },
                    new CustomerContact { Name = "Sam", Phone = "0771234567", Usage = ContactUsage.NotificationsOnly },
                ],
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var customer = await db.Customers.Include(c => c.Contacts).FirstAsync(c => c.Id == customerId);
            customer.Contacts.Should().HaveCount(2);
            customer.Contacts.Should().ContainSingle(c => c.Usage == ContactUsage.DocumentsAndNotifications).Which.Name.Should().Be("Priya");
            customer.Contacts.Single(c => c.Name == "Sam").Phone.Should().Be("0771234567");
        }

        // Reconcile-replace (what the save path does): clearing the loaded collection deletes the removed
        // rows outright — contacts are the customer's list, not history to keep — and the new ones insert.
        await using (var db = _fixture.CreateContext(change))
        {
            var customer = await db.Customers.Include(c => c.Contacts).FirstAsync(c => c.Id == customerId);
            customer.Contacts.Clear();
            customer.Contacts.Add(new CustomerContact { Name = "Nadia", Email = "nadia@acme.test", Usage = ContactUsage.DocumentsAndNotifications });
            await db.SaveChangesAsync();
        }

        await using (var db = _fixture.CreateContext(change))
        {
            var contacts = await db.CustomerContacts.Where(c => c.CustomerId == customerId).ToListAsync();
            contacts.Should().ContainSingle().Which.Name.Should().Be("Nadia"); // the two originals were deleted
        }
    }
}
