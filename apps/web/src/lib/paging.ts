/**
 * One page of a list, as the server returns it.
 *
 * Declared here rather than taken from the generated client because the generated type is per
 * endpoint (`PagedResultOfInvoiceSummary`, and one more for every list that pages) — the shape is
 * the same for all of them, and one generic name reads better at the call sites.
 */
export interface Paged<T> {
  rows: T[];

  /**
   * How many rows match the current search across every page.
   *
   * Not the table's size: with a search applied this is the size of the result, which is what the
   * pager has to count pages from.
   */
  total: number;

  /** 1-based. */
  page: number;
  pageSize: number;
}

/** What a list page holds while the user is paging and searching. */
export const FIRST_PAGE = 1;

/** The default page size, matching the server's. */
export const DEFAULT_PAGE_SIZE = 25;
