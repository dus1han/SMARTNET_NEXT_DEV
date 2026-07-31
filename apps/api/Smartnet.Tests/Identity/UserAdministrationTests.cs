using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Smartnet.Api.Contracts;
using Smartnet.Api.Controllers;
using Smartnet.Domain.Auditing;
using Smartnet.Domain.Documents;
using Smartnet.Domain.Identity;
using Smartnet.Domain.MasterData;
using Smartnet.Domain.Settings;
using Smartnet.Infrastructure.Auditing;
using Smartnet.Infrastructure.Exporting;
using Smartnet.Infrastructure.Identity;
using Smartnet.Infrastructure.Persistence;
using Smartnet.Tests.Auditing;

namespace Smartnet.Tests.Identity;

/// <summary>
/// The three things an administrator can do to an account beyond granting it access: rename it,
/// switch it back on, and — while it is still a mistake rather than a record — remove it outright.
/// </summary>
/// <remarks>
/// <para>All three turn on one question: <b>has this account raised anything?</b> A username is what
/// the legacy app logs in with and what every document's <c>preparedby</c> was written against, so
/// correcting a typo on the day an account is made is a fix and changing it afterwards rewrites who
/// those documents came from. Deleting outright is the same question with a harder answer.</para>
///
/// <para>The controller is exercised directly rather than over HTTP. What is under test is the rule
/// and what it does to the database — not routing, which
/// <see cref="EndpointAuthorizationTests"/> covers.</para>
/// </remarks>
[Collection(nameof(AuditCollection))]
public sealed class UserAdministrationTests
{
    private readonly AuditFixture _fixture;
    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>The administrator doing the changing — never the account being changed.</summary>
    private const long ActingAdminId = 999_001;

    /// <summary>
    /// Ids for the accounts these tests create, assigned rather than left to AUTO_INCREMENT.
    /// </summary>
    /// <remarks>
    /// The fixture is one database shared by the whole collection, and what is under test here is
    /// "has anyone attributed anything to this id?". An auto-increment id starts at 1 and every other
    /// test in the collection saves rows as <c>FakeChangeContext.UserId = 1</c> — so a freshly created
    /// account would inherit somebody else's documents and be judged to have transacted. Numbering
    /// from 900,000 puts these accounts outside the range anything else uses.
    /// </remarks>
    private static long _nextUserId = 900_000;

    public UserAdministrationTests(AuditFixture fixture) => _fixture = fixture;

    // --- Renaming --------------------------------------------------------------------------------

    [Fact]
    public async Task A_username_can_be_corrected_while_the_account_has_raised_nothing()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-chnaka", "Chanaka Perera");

        var result = await ControllerFor(db).Update(
            user.Id,
            new UpdateUserRequest("Chanaka Perera", [], user.RowVersion, Username: "uat-chanaka"),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        var saved = await Reload(user.Id);
        saved.Username.Should().Be("uat-chanaka");
        saved.Name.Should().Be("Chanaka Perera");
    }

    [Fact]
    public async Task A_username_is_fixed_once_the_account_has_raised_a_document()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-raised-things", "Nimal Silva");
        await GivenInvoiceRaisedBy(db, user.Id);

        var result = await ControllerFor(db).Update(
            user.Id,
            new UpdateUserRequest("Nimal Silva", [], user.RowVersion, Username: "something-else"),
            CancellationToken.None);

        // 409, not a silent no-op: the administrator asked for something and has to be told it did
        // not happen, or they will believe the login they typed is now the login that works.
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        (await Reload(user.Id)).Username.Should().Be("uat-raised-things");
    }

    [Fact]
    public async Task The_full_name_is_editable_after_transactions_when_the_username_is_not()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-settled", "Nmial Silva"); // misspelt
        await GivenInvoiceRaisedBy(db, user.Id);

        // The whole point of the rule being about the username alone: a misspelt display name is
        // still worth fixing on an account that has been working for a year.
        var result = await ControllerFor(db).Update(
            user.Id,
            new UpdateUserRequest("Nimal Silva", [], user.RowVersion),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        var saved = await Reload(user.Id);
        saved.Name.Should().Be("Nimal Silva");
        saved.Username.Should().Be("uat-settled");
    }

    [Fact]
    public async Task A_username_already_in_use_is_refused()
    {
        await using var db = _fixture.CreateContext(Change());
        var taken = await GivenUser(db, "uat-taken-already", "Someone Else");
        var user = await GivenUser(db, "uat-wants-it", "Hopeful Person");

        var result = await ControllerFor(db).Update(
            user.Id,
            new UpdateUserRequest("Hopeful Person", [], user.RowVersion, Username: taken.Username),
            CancellationToken.None);

        result.Should().BeOfType<ConflictObjectResult>();
        (await Reload(user.Id)).Username.Should().Be("uat-wants-it");
    }

    [Fact]
    public async Task Renaming_keeps_the_roles_the_account_already_holds()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-keeps-roles", "Role Holder");
        var roleId = await GivenRole(db, user.Id, "Sales");

        // The endpoint sets the WHOLE role set, so the screen sends back what the account already
        // holds. This is the regression that would strip somebody's access as a side effect of
        // fixing a spelling mistake.
        var result = await ControllerFor(db).Update(
            user.Id,
            new UpdateUserRequest("Role Holder Renamed", [roleId], user.RowVersion),
            CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        await using var check = _fixture.CreateContext(Change());
        (await check.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == roleId))
            .Should().BeTrue();
    }

    // --- Enabling --------------------------------------------------------------------------------

    [Fact]
    public async Task A_disabled_account_can_be_enabled_again()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-coming-back", "Returning Person");

        (await ControllerFor(db).Disable(user.Id, CancellationToken.None))
            .Should().BeOfType<NoContentResult>();

        (await Reload(user.Id)).IsDisabled.Should().BeTrue();

        await using var second = _fixture.CreateContext(Change());
        var result = await ControllerFor(second).Enable(user.Id, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        var saved = await Reload(user.Id);
        saved.IsDisabled.Should().BeFalse();
        saved.DeletedAt.Should().BeNull();

        // Both halves of Disable undone — the legacy app reads ustat and nothing else.
        saved.Ustat.Should().Be("Active");
    }

    [Fact]
    public async Task Enabling_an_account_that_is_already_active_is_refused()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-never-left", "Present Person");

        var result = await ControllerFor(db).Enable(user.Id, CancellationToken.None);

        // Otherwise the audit trail gains an "enabled" entry for an account that was never off.
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_user_disabled_only_in_the_legacy_app_can_be_enabled_here()
    {
        await using var db = _fixture.CreateContext(Change());

        // ustat alone, no soft delete: how the old app switched somebody off. IsDisabled counts it,
        // so Enable has to clear it or the account cannot be brought back from this screen at all.
        var user = await GivenUser(db, "uat-legacy-off", "Old System Person");
        user.Ustat = "Inactive";
        await db.SaveChangesAsync();

        (await ControllerFor(_fixture.CreateContext(Change())).Enable(user.Id, CancellationToken.None))
            .Should().BeOfType<NoContentResult>();

        (await Reload(user.Id)).IsDisabled.Should().BeFalse();
    }

    // --- Deleting outright -----------------------------------------------------------------------

    [Fact]
    public async Task An_account_that_raised_nothing_can_be_deleted_outright()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-created-by-mistake", "Typo Person");
        var roleId = await GivenRole(db, user.Id, "Sales");

        db.UserPermissionOverrides.Add(new UserPermissionOverride
        {
            UserId = user.Id,
            Permission = Permissions.CustomerM,
            Granted = true,
        });
        await db.SaveChangesAsync();

        var result = await ControllerFor(_fixture.CreateContext(Change()))
            .DeletePermanently(user.Id, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();

        await using var check = _fixture.CreateContext(Change());

        // Gone for real, not soft-deleted — IgnoreQueryFilters would still find a soft delete.
        (await check.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == user.Id)).Should().BeFalse();

        // And nothing left pointing at an id that no longer resolves.
        (await check.UserRoles.IgnoreQueryFilters().AnyAsync(r => r.UserId == user.Id)).Should().BeFalse();
        (await check.UserPermissionOverrides.IgnoreQueryFilters().AnyAsync(o => o.UserId == user.Id))
            .Should().BeFalse();

        _ = roleId; // the role itself survives; only the assignment goes
        (await check.Roles.AnyAsync(r => r.Id == roleId)).Should().BeTrue();
    }

    [Fact]
    public async Task The_deletion_is_recorded_with_the_name_of_who_was_deleted()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-about-to-go", "Departing Person");

        await ControllerFor(_fixture.CreateContext(Change()))
            .DeletePermanently(user.Id, CancellationToken.None);

        await using var check = _fixture.CreateContext(Change());
        var key = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var entry = await check.AuditLog
            .Where(a => a.EntityType == nameof(User) && a.EntityId == key && a.Action == AuditAction.Delete)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        entry.Should().NotBeNull("a permanent deletion that leaves no trace is the one thing worse than it");

        // The id resolves to nobody now, so the entry has to carry the name itself or it names no one.
        entry!.Changes.Should().Contain("uat-about-to-go");
        entry.Changes.Should().Contain("Departing Person");
    }

    [Fact]
    public async Task An_account_that_raised_something_cannot_be_deleted_outright()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-has-history", "Busy Person");
        await GivenInvoiceRaisedBy(db, user.Id);

        var result = await ControllerFor(_fixture.CreateContext(Change()))
            .DeletePermanently(user.Id, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        (await Reload(user.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_voided_document_still_counts_as_having_raised_something()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-voided-it-all", "Second Thoughts");
        var invoiceId = await GivenInvoiceRaisedBy(db, user.Id);

        // Void it. Voiding must not hand back the right to delete the person who raised it — the
        // row still carries their id, and the document still happened.
        var invoice = await db.Invoices.IgnoreQueryFilters().FirstAsync(i => i.Id == invoiceId);
        invoice.DeletedAt = Clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();

        var result = await ControllerFor(_fixture.CreateContext(Change()))
            .DeletePermanently(user.Id, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Nobody_can_delete_their_own_account()
    {
        await using var db = _fixture.CreateContext(Change());
        var user = await GivenUser(db, "uat-self-destruct", "Reckless Admin");

        // Acting as themselves.
        var result = await ControllerFor(db, actingAs: user.Id)
            .DeletePermanently(user.Id, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        (await Reload(user.Id)).Should().NotBeNull();
    }

    // --- What the screen is told ------------------------------------------------------------------

    [Fact]
    public async Task The_list_says_which_accounts_have_raised_something()
    {
        await using var db = _fixture.CreateContext(Change());
        var fresh = await GivenUser(db, "uat-brand-new-account", "Fresh Start");
        var busy = await GivenUser(db, "uat-working-account", "Been Here A While");
        await GivenInvoiceRaisedBy(db, busy.Id);

        var response = await ControllerFor(_fixture.CreateContext(Change()))
            .List(CancellationToken.None);

        // The action returns Ok(...), so the payload is on Result, not Value.
        var users = (IReadOnlyList<UserSummary>)response.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value!;

        // This is what decides whether the screen offers the username field and the delete item.
        users.Single(u => u.Id == fresh.Id).HasTransactions.Should().BeFalse();
        users.Single(u => u.Id == busy.Id).HasTransactions.Should().BeTrue();
    }

    // --- Seeding ----------------------------------------------------------------------------------

    private static FakeChangeContext Change() =>
        new() { UserId = ActingAdminId, Reason = "Tidying up the user list" };

    private static UsersController ControllerFor(TestDbContext db, long actingAs = ActingAdminId)
    {
        var controller = new UsersController(
            db,
            new PermissionService(db),
            new Argon2PasswordHasher(),
            new ExcelExporter(),
            new AuditWriter(db, Change(), Clock),
            Clock);

        // CurrentUserId reads the NameIdentifier claim — "you cannot delete your own account" is not
        // a rule that can be tested without one.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, actingAs.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                    authenticationType: "test")),
            },
        };

        return controller;
    }

    private async Task<User> Reload(long id)
    {
        await using var db = _fixture.CreateContext(Change());
        return (await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id))!;
    }

    private static async Task<User> GivenUser(TestDbContext db, string username, string name)
    {
        var user = new User
        {
            Id = Interlocked.Increment(ref _nextUserId),
            Username = username,
            Name = name,
            Ustat = "Active",
            PasswordHash = "not-a-real-hash",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<long> GivenRole(TestDbContext db, long userId, string name)
    {
        var role = new Role { Name = $"{name}-{userId}", IsSystem = false };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();

        return role.Id;
    }

    /// <summary>An invoice attributed to this user — the thing that closes the window.</summary>
    private static async Task<long> GivenInvoiceRaisedBy(TestDbContext db, long userId)
    {
        // Codes carry the user id, which these tests number from 900,000 — so they cannot collide with
        // the "C-1", "C-2" the rest of the collection seeds into the same database.
        var company = new Company { Name = $"Company for {userId}", VatCode = "1" };
        db.Companies.Add(company);

        var customer = new Customer { Code = $"C-{userId}", Name = "Acme" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            CompanyId = company.Id,
            CustomerId = customer.Id,
            Number = $"INV-{userId}",
            Date = new DateOnly(2026, 7, 15),
            Type = InvoiceType.Credit,
            DataOrigin = "new",
            PreparedBy = userId,
            // NOT NULL in the legacy schema, like the two shadow columns below.
            ContactPerson = string.Empty,
            Subtotal = 100m,
            NetTotal = 100m,
            Total = 100m,
        };

        db.Invoices.Add(invoice);

        // invoice_h keeps its legacy varchar columns, and three of them are NOT NULL — the save
        // pipeline writes them alongside the typed ones. This seed goes around the pipeline, so it
        // has to write them itself or the insert is rejected.
        var entry = db.Entry(invoice);
        entry.Property("discountper").CurrentValue = "0";
        entry.Property("beforedisctot").CurrentValue = "100";

        await db.SaveChangesAsync();

        return invoice.Id;
    }
}
