"use client";

/**
 * One customer's whole account — the page every dashboard panel points at.
 *
 * The dashboard raises questions it cannot answer: it says this customer owes 1,174,480 and last bought
 * thirteen months ago, and then leaves you. This is what somebody needs before picking up the phone —
 * which invoices, what was ever paid, how they normally pay, and when they stopped.
 *
 * Keyed by the legacy customer *code*, because that is what the documents carry and what the panels
 * already hold.
 */

import { useQuery } from "@tanstack/react-query";
import { useParams } from "next/navigation";
import Link from "next/link";
import { AlertTriangle, ArrowLeft, Clock, Coins, Wallet } from "lucide-react";
import { ApiError } from "@/lib/api";
import { getCustomerInsight } from "@/lib/dashboard";
import { PageHeader } from "@/components/shell/app-shell";
import { ANALYTICS_VIZ_CSS, MonthlyTrendChart } from "@/components/dashboard/analytics-charts";
import { StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Badge, Card, CardHeader, ErrorBanner, FadeIn, LoadingPanel } from "@/components/ui";

export default function CustomerInsightPage() {
  const { code } = useParams<{ code: string }>();

  const insight = useQuery({
    queryKey: ["customer-insight", code],
    queryFn: () => getCustomerInsight(code),
    enabled: Boolean(code),
  });

  const error = insight.error as ApiError | null;
  const c = insight.data;

  return (
    <FadeIn className="space-y-6">
      <style>{ANALYTICS_VIZ_CSS}</style>

      <Link
        href="/"
        className="inline-flex items-center gap-1.5 text-sm text-muted transition-colors hover:text-text"
      >
        <ArrowLeft className="size-4" aria-hidden />
        Dashboard
      </Link>

      {error && <ErrorBanner message={error.message} correlationId={error.correlationId} />}

      {insight.isPending ? (
        <LoadingPanel />
      ) : c ? (
        <>
          <div className="flex flex-wrap items-start justify-between gap-4">
            <PageHeader
              title={c.name}
              description={[c.code, c.phone, c.vatNumber && `VAT ${c.vatNumber}`].filter(Boolean).join(" · ")}
            />
            {/* Silence is the first thing to know about an account, so it is stated beside the name. */}
            {typeof c.silentDays === "number" && c.silentDays > 90 && (
              <Badge tone="warning">Silent {c.silentDays} days</Badge>
            )}
          </div>

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Lifetime value"
              icon={Coins}
              color="indigo"
              value={<AnimatedNumber value={c.lifetime} format={formatMoney} />}
              sub={`${c.invoiceCount} invoice${c.invoiceCount === 1 ? "" : "s"}`}
            />
            <StatTile
              label="Outstanding"
              icon={Wallet}
              color="violet"
              delayMs={70}
              value={<AnimatedNumber value={c.outstanding} format={formatMoney} />}
              sub={
                c.creditLimit
                  ? c.outstanding > c.creditLimit
                    ? `over their ${formatMoney(c.creditLimit)} limit`
                    : `within their ${formatMoney(c.creditLimit)} limit`
                  : "no credit limit recorded"
              }
            />
            <StatTile
              label="Overdue"
              icon={AlertTriangle}
              color="amber"
              delayMs={140}
              value={<AnimatedNumber value={c.overdue} format={formatMoney} />}
              sub={c.overdue > 0 ? "past 30 days" : "nothing late"}
            />
            <StatTile
              label="Pays in"
              icon={Clock}
              color="sky"
              delayMs={210}
              value={c.daysToCollect === null ? "—" : `${c.daysToCollect} days`}
              sub={c.daysToCollect === null ? "nothing settled yet" : "their own average"}
            />
          </div>

          <Card>
            <CardHeader
              title="What they buy"
              description={
                c.firstPurchase
                  ? `Twelve months. First bought ${formatReportDate(c.firstPurchase)}${
                      c.lastPurchase ? `, last ${formatReportDate(c.lastPurchase)}.` : "."
                    }`
                  : "Twelve months."
              }
            />
            <MonthlyTrendChart points={c.monthlyTrend} />
          </Card>

          <Card>
            <CardHeader title="Invoices" description="Newest first. An account is read backwards from what happened last." />
            {c.invoices.length === 0 ? (
              <Empty>No invoices for this customer.</Empty>
            ) : (
              <div className="max-h-96 overflow-auto">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-surface">
                    <tr className="border-b border-subtle text-left">
                      <Th>Invoice</Th>
                      <Th>Date</Th>
                      <Th>Terms</Th>
                      <Th align="right">Total</Th>
                      <Th align="right">Outstanding</Th>
                      <Th align="right">Age</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {c.invoices.map((i) => (
                      <tr key={i.number} className="border-b border-subtle/60 last:border-0">
                        <td className="py-2.5 pr-4 font-medium text-text">{i.number}</td>
                        <td className="whitespace-nowrap py-2.5 pr-4 text-muted">
                          {i.date ? formatReportDate(i.date) : "—"}
                        </td>
                        <td className="py-2.5 pr-4 text-muted">{i.type || "—"}</td>
                        <td className="tabular py-2.5 pr-4 text-right text-text">{formatMoney(i.total)}</td>
                        <td
                          className={
                            i.balance > 0
                              ? "tabular py-2.5 pr-4 text-right font-medium text-warning-text"
                              : "tabular py-2.5 pr-4 text-right text-muted"
                          }
                        >
                          {i.balance > 0 ? formatMoney(i.balance) : "settled"}
                        </td>
                        <td className="tabular whitespace-nowrap py-2.5 text-right text-muted">
                          {/* Age only means something while money is still on it. */}
                          {i.balance > 0 ? `${i.ageDays} days` : "—"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <Card>
            <CardHeader title="Payments" description="Everything received against this account." />
            {c.payments.length === 0 ? (
              <Empty>Nothing has been received from this customer.</Empty>
            ) : (
              <div className="max-h-80 overflow-auto">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-surface">
                    <tr className="border-b border-subtle text-left">
                      <Th>Date</Th>
                      <Th>Against</Th>
                      <Th>Method</Th>
                      <Th align="right">Amount</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {c.payments.map((p, i) => (
                      <tr key={`${p.invoiceNo}-${p.date}-${i}`} className="border-b border-subtle/60 last:border-0">
                        <td className="whitespace-nowrap py-2.5 pr-4 text-muted">
                          {p.date ? formatReportDate(p.date) : "—"}
                        </td>
                        <td className="py-2.5 pr-4 text-text">{p.invoiceNo || "—"}</td>
                        <td className="py-2.5 pr-4 text-muted">{p.method}</td>
                        <td className="tabular py-2.5 text-right text-text">{formatMoney(p.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      ) : null}
    </FadeIn>
  );
}

function Th({ children, align = "left" }: { children: React.ReactNode; align?: "left" | "right" }) {
  return (
    <th className={align === "right" ? "pb-2 pr-4 text-right font-medium text-muted" : "pb-2 pr-4 font-medium text-muted"}>
      {children}
    </th>
  );
}

function Empty({ children }: { children: string }) {
  return (
    <div className="flex h-32 items-center justify-center rounded-lg border border-dashed border-subtle text-sm text-muted">
      {children}
    </div>
  );
}
