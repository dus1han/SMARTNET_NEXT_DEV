import { Skeleton } from "@/components/ui";

/**
 * What a signed-in screen shows while its route arrives.
 *
 * The server is a round trip from the office (France, ~150 ms each way), and a screen whose URL holds an
 * id — an invoice, a quotation — cannot be fully prefetched. Without this file Next.js does not prefetch
 * such a route at all, so a click waits out that trip with nothing changing on screen. With it, the route
 * is partially prefetched and this skeleton appears at once; the page swaps in when it is ready.
 */
export default function Loading() {
  return (
    <div className="space-y-6" aria-busy="true" aria-label="Loading">
      <Skeleton className="h-4 w-32" />
      <div className="space-y-2">
        <Skeleton className="h-7 w-64" />
        <Skeleton className="h-4 w-96 max-w-full" />
      </div>
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <Skeleton className="h-20" />
        <Skeleton className="h-20" />
        <Skeleton className="h-20" />
        <Skeleton className="h-20" />
      </div>
      <Skeleton className="h-64" />
    </div>
  );
}
