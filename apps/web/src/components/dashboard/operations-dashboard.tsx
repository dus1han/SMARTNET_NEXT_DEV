"use client";

/**
 * The operations dashboard — what somebody serving customers needs, and nothing about what the
 * business earns.
 *
 * **Defined by what it withholds.** No profit, no margin, no cost, no supplier spend, no customer
 * lifetime value or concentration. What is left is not a cut-down management view but a different
 * question: not "how are we doing" but "what should I do, and can I sell to this person".
 *
 * Company-wide rather than per-user, deliberately. A clerk needs to know a customer is ninety days late
 * and over their limit *before* selling to them on credit, and whose name is on the last invoice has
 * nothing to do with that.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { AlertTriangle, FileText, ReceiptText, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getOperationsDashboard, type CompanyFilter } from "@/lib/dashboard";
import { PageHeader } from "@/components/shell/app-shell";
import { ANALYTICS_VIZ_CSS, AgeingChart } from "@/components/dashboard/analytics-charts";
import { CompanySwitch } from "@/components/dashboard/company-switch";
import { StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Card, CardHeader, ErrorBanner, FadeIn, LoadingPanel } from "@/components/ui";

export function OperationsDashboard() {
  const [company, setCompany] = useState<CompanyFilter>("all");

  const ops = useQuery({
    queryKey: ["dashboard-operations", company],
    queryFn: () => getOperationsDashboard(company),
    placeholderData: keepPreviousData,
  });

  const loadError = ops.error as ApiError | null;
  const o = ops.data;

  return (
    <FadeIn className="space-y-6">
      <style>{ANALYTICS_VIZ_CSS}</style>

      <PageHeader
        title="Dashboard"
        description="Today's work, and what is still to be collected."
        actions={<CompanySwitch value={company} onChange={setCompany} />}
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      {ops.isPending ? (
        <LoadingPanel />
      ) : o ? (
        <>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Invoices today"
              icon={FileText}
              color="indigo"
              value={<AnimatedNumber value={o.invoicesToday} format={(n) => String(Math.round(n))} />}
              sub="raised so far today"
            />
            <StatTile
              label="Sales this month"
              icon={ReceiptText}
              color="sky"
              delayMs={70}
              value={<AnimatedNumber value={o.salesThisMonth} format={formatMoney} />}
              sub={`${o.invoicesThisMonth} invoice${o.invoicesThisMonth === 1 ? "" : "s"}`}
            />
            <StatTile
              label="Still to collect"
              icon={Wallet}
              color="violet"
              delayMs={140}
              value={<AnimatedNumber value={o.toCollect} format={formatMoney} />}
              sub="everything outstanding"
            />
            <StatTile
              label="Overdue"
              icon={AlertTriangle}
              color="amber"
              delayMs={210}
              value={<AnimatedNumber value={o.overdue} format={formatMoney} />}
              sub={o.toCollect > 0 ? `${Math.round((o.overdue / o.toCollect) * 100)}% of what is owed` : "nothing owed"}
            />
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card className="h-full">
              <CardHeader
                title="Where the money is owed"
                description="Open balances by age. Anything past the first bar has stopped moving."
              />
              <AgeingChart buckets={o.ageing} />

              {o.overdueByCustomer.length > 0 && (
                <div className="mt-5 border-t border-subtle pt-4">
                  <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-muted">Who to chase</p>
                  <ul className="space-y-2">
                    {o.overdueByCustomer.map((c) => (
                      <li key={c.code} className="flex items-baseline justify-between gap-3 text-sm">
                        <Link
                          href={`/customers/${encodeURIComponent(c.code)}`}
                          className="truncate text-text underline-offset-2 hover:underline"
                          title={c.name}
                        >
                          {c.name}
                        </Link>
                        <span className="shrink-0 text-right">
                          <span className="tabular text-text">{formatMoney(c.owed)}</span>
                          <span className="ml-2 text-xs text-muted">
                            {c.invoices} inv · oldest {c.oldestDays}d
                          </span>
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {/*
                The reason this screen exists. Somebody at the counter is about to sell on credit to a
                customer who is already past their limit, and this is the only place that says so.
              */}
              {o.overCreditLimit.length > 0 && (
                <div className="mt-4 border-t border-subtle pt-4">
                  <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-warning-text">
                    Over their credit limit
                  </p>
                  <ul className="space-y-2">
                    {o.overCreditLimit.map((c) => (
                      <li key={c.code} className="flex items-baseline justify-between gap-3 text-sm">
                        <Link
                          href={`/customers/${encodeURIComponent(c.code)}`}
                          className="truncate text-text underline-offset-2 hover:underline"
                          title={c.name}
                        >
                          {c.name}
                        </Link>
                        <span className="shrink-0 text-right">
                          <span className="tabular text-text">{formatMoney(c.owed)}</span>
                          <span className="ml-2 text-xs text-muted">limit {formatMoney(c.limit)}</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </Card>

            <Card className="h-full">
              <CardHeader title="Latest invoices" description="The last ten raised, across the company." />
              {o.recentInvoices.length === 0 ? (
                <div className="flex h-32 items-center justify-center rounded-lg border border-dashed border-subtle text-sm text-muted">
                  No invoices raised yet.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-subtle text-left">
                        <th className="pb-2 pr-4 font-medium text-muted">Invoice</th>
                        <th className="pb-2 pr-4 font-medium text-muted">Customer</th>
                        <th className="pb-2 pr-4 font-medium text-muted">Date</th>
                        <th className="pb-2 text-right font-medium text-muted">Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {o.recentInvoices.map((r) => (
                        <tr key={r.number} className="border-b border-subtle/60 last:border-0">
                          <td className="py-2.5 pr-4 font-medium text-text">{r.number}</td>
                          <td className="max-w-[14rem] truncate py-2.5 pr-4 text-muted" title={r.customer}>
                            {r.customer || "—"}
                          </td>
                          <td className="whitespace-nowrap py-2.5 pr-4 text-muted">
                            {r.date ? formatReportDate(r.date) : "—"}
                          </td>
                          <td className="tabular py-2.5 text-right text-text">{formatMoney(r.total)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>
        </>
      ) : null}
    </FadeIn>
  );
}
