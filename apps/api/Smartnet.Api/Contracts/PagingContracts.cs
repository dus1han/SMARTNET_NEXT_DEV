using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Smartnet.Api.Contracts;

// --- Server-side paging -------------------------------------------------------------------------
//
// Lists used to be returned whole and paged in the browser, which was right while every list was
// small. Invoices is 2,485 rows and quotations 2,119, and both only grow — so the long lists now
// page on the server and the short ones (cheques, expenses, job cards: single digits) do not.

/// <summary>One page of a list, with enough context for the pager to describe the whole.</summary>
/// <remarks>
/// <see cref="Total"/> is the count <em>after</em> filtering, not the table's size — it is what the
/// pager counts pages from, so a search that matches three rows must say three.
/// </remarks>
public sealed record PagedResult<T>(IReadOnlyList<T> Rows, int Total, int Page, int PageSize);

/// <summary>The paging, searching and sorting a list request carries.</summary>
/// <remarks>
/// Bound from the query string rather than a body: a list is a GET, and a page of results should be
/// something you can link to.
/// </remarks>
public sealed record PageRequest
{
    /// <summary>The largest page the server will serve, whatever is asked for.</summary>
    /// <remarks>
    /// Without a ceiling, <c>?pageSize=100000</c> reintroduces exactly the whole-table read this
    /// exists to prevent — from outside, on demand.
    /// </remarks>
    public const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    /// <summary>Free text the caller typed. Matched against whatever the endpoint decides is searchable.</summary>
    public string? Search { get; init; }

    /// <summary>Page index clamped to something sane — page 0 and page −3 are the same page 1.</summary>
    [BindNever]
    public int SafePage => Page < 1 ? 1 : Page;

    /// <summary>Page size clamped to <see cref="MaxPageSize"/> and at least 1.</summary>
    [BindNever]
    public int SafePageSize => PageSize < 1 ? 25 : PageSize > MaxPageSize ? MaxPageSize : PageSize;

    /// <summary>Rows to skip for this page.</summary>
    [BindNever]
    public int Skip => (SafePage - 1) * SafePageSize;

    /// <summary>The search term trimmed, or null when it is not worth filtering on.</summary>
    [BindNever]
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

    /// <summary>
    /// The term as a SQL LIKE pattern, with the wildcards a user typed treated as literals.
    /// </summary>
    /// <remarks>
    /// A customer called "100% Cotton" is searchable; without escaping, the <c>%</c> would match
    /// everything and the search would look broken rather than wrong.
    /// </remarks>
    [BindNever]
    public string? LikePattern => Term is null
        ? null
        : "%" + Term.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    /// <summary>The accessible company ids as the text the legacy tables store them in.</summary>
    public static HashSet<string> AsText(IEnumerable<long> ids) =>
        ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
}
