"use client";

/**
 * The dashboard — what the business looks like, not what it totalled.
 *
 * Each reading answers a question somebody actually asks: which way are we going, where has the money
 * stopped moving, is cash keeping up with profit, how long do customers take, who are we dependent on.
 * The earlier month-at-a-glance tiles and the daily-sales bars are gone — they showed a total with no
 * direction and an "outstanding" figure that concealed the ageing behind it.
 *
 * There is no best-selling-lines panel, deliberately. Not one of the 12,598 invoice lines carries an
 * item code, and 7,029 distinct descriptions across them means the text is close to unique per line —
 * so any ranking would be counting spellings, not products.
 *
 * The company switch lives in the page header rather than a filter card of its own: with three options
 * a dropdown hid two of them for no gain, and the card cost the row that pushed the first chart below
 * the fold.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, Banknote, CalendarClock, Clock, Coins, FileText, TrendingUp, UserPlus, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getDashboardAnalytics, type CompanyFilter } from "@/lib/dashboard";
import { PageHeader } from "@/components/shell/app-shell";
import {
  ANALYTICS_VIZ_CSS,
  AgeingChart,
  CashFlowChart,
  Delta,
  MonthlyTrendChart,
  RankedBars,
} from "@/components/dashboard/analytics-charts";
import { CompanySwitch } from "@/components/dashboard/company-switch";
import { StatTile, formatMoney, formatReportDate } from "@/components/reports";
import { AnimatedNumber, Card, CardHeader, ErrorBanner, FadeIn, LoadingPanel } from "@/components/ui";

export default function DashboardPage() {
  const [company, setCompany] = useState<CompanyFilter>("all");

  const analytics = useQuery({
    queryKey: ["dashboard-analytics", company],
    queryFn: () => getDashboardAnalytics(company),
    // Hold the current figures while a company switch loads, so the tiles count to the new numbers
    // rather than collapsing to a spinner and back.
    placeholderData: keepPreviousData,
  });

  const loadError = analytics.error as ApiError | null;
  const a = analytics.data;
  const owed = a ? a.ageing.reduce((sum, b) => sum + b.amount, 0) : 0;

  return (
    <FadeIn className="space-y-6">
      <style>{ANALYTICS_VIZ_CSS}</style>

      <PageHeader
        title="Dashboard"
        description="How the business is doing, and where it needs attention."
        actions={<CompanySwitch value={company} onChange={setCompany} />}
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      {analytics.isPending ? (
        <LoadingPanel />
      ) : a ? (
        <>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Revenue this month"
              icon={TrendingUp}
              color="indigo"
              value={<AnimatedNumber value={a.revenue.value} format={formatMoney} />}
              sub={<Delta change={a.revenue.changePercent} />}
            />
            <StatTile
              label="Gross profit"
              icon={Coins}
              color="emerald"
              delayMs={70}
              value={<AnimatedNumber value={a.grossProfit.value} format={formatMoney} />}
              sub={
                <>
                  <span className="tabular">{a.marginPercent.toFixed(1)}% margin</span>
                  {" · "}
                  <Delta change={a.grossProfit.changePercent} />
                </>
              }
            />
            <StatTile
              label="Collected this month"
              icon={Wallet}
              color="violet"
              delayMs={140}
              value={<AnimatedNumber value={a.collected.value} format={formatMoney} />}
              sub={<Delta change={a.collected.changePercent} />}
            />
            <StatTile
              label="Overdue"
              icon={AlertTriangle}
              color="amber"
              delayMs={210}
              value={<AnimatedNumber value={a.overdue} format={formatMoney} />}
              sub={owed > 0 ? `${Math.round((a.overdue / owed) * 100)}% of everything owed` : "nothing owed"}
            />
          </div>

          <Reveal delayMs={90}>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
              <StatTile
                label="Days to collect"
                icon={Clock}
                color="sky"
                value={a.daysToCollect === null ? "—" : `${a.daysToCollect} days`}
                sub={a.daysToCollect === null ? "nothing settled yet" : "average over twelve months"}
              />
              <StatTile
                label="Invoices this month"
                icon={FileText}
                color="slate"
                delayMs={70}
                value={<AnimatedNumber value={a.invoiceCount} format={(n) => String(Math.round(n))} />}
                sub={`averaging ${formatMoney(a.averageInvoice)}`}
              />
              <StatTile
                label="Cash sales"
                icon={Banknote}
                color="emerald"
                delayMs={140}
                value={<AnimatedNumber value={a.mix.cash} format={formatMoney} />}
                sub={`${a.mix.cashCount} invoice${a.mix.cashCount === 1 ? "" : "s"}, settled at the counter`}
              />
              <StatTile
                label="New customers"
                icon={UserPlus}
                color="rose"
                delayMs={280}
                value={<AnimatedNumber value={a.newCustomers.value} format={(n) => String(Math.round(n))} />}
                sub={<Delta change={a.newCustomers.changePercent} />}
              />
              <StatTile
                label="Credit sales"
                icon={CalendarClock}
                color="amber"
                delayMs={210}
                value={<AnimatedNumber value={a.mix.credit} format={formatMoney} />}
                sub={`${a.mix.creditCount} invoice${a.mix.creditCount === 1 ? "" : "s"}, still to collect`}
              />
            </div>
          </Reveal>

          <Reveal delayMs={150}>
            <Card>
              <CardHeader
                title="Revenue and profit"
                description="Twelve months. The gap between the pair is what the sales cost you."
              />
              <MonthlyTrendChart points={a.monthlyTrend} />
            </Card>
          </Reveal>

          <div className="grid gap-4 lg:grid-cols-2">
            <Reveal delayMs={200}>
              <Card className="h-full">
                <CardHeader
                  title="Where the money is owed"
                  description="Open balances by age. Anything past the first bar has stopped moving."
                />
                <AgeingChart buckets={a.ageing} />

                {a.overdueByCustomer.length > 0 && (
                  <div className="mt-5 border-t border-subtle pt-4">
                    <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-muted">
                      Who to chase
                    </p>
                    <ul className="space-y-2">
                      {a.overdueByCustomer.map((c) => (
                        <li key={c.name} className="flex items-baseline justify-between gap-3 text-sm">
                          <span className="truncate text-text" title={c.name}>{c.name}</span>
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

                {a.overCreditLimit.length > 0 && (
                  <div className="mt-4 border-t border-subtle pt-4">
                    <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-warning-text">
                      Over their credit limit
                    </p>
                    <ul className="space-y-2">
                      {a.overCreditLimit.map((c) => (
                        <li key={c.name} className="flex items-baseline justify-between gap-3 text-sm">
                          <span className="truncate text-text" title={c.name}>{c.name}</span>
                          <span className="shrink-0 text-right">
                            <span className="tabular text-text">{formatMoney(c.owed)}</span>
                            <span className="ml-2 text-xs text-muted">
                              limit {formatMoney(c.limit)}
                            </span>
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </Card>
            </Reveal>

            <Reveal delayMs={260}>
              <Card className="h-full">
                <CardHeader
                  title="Cash in and out"
                  description="Receipts against supplier payments and expenses. Profit is not cash."
                />
                <CashFlowChart points={a.cashFlow} />
                <CashSummary points={a.cashFlow} />
              </Card>
            </Reveal>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Reveal delayMs={320}>
              <Card className="h-full">
                <CardHeader
                  title="Biggest customers this month"
                  description={`The top five are ${a.topCustomerShare.toFixed(1)}% of the month’s revenue.`}
                />
                <RankedBars
                  rows={a.topCustomers.map((c) => ({ label: c.name, value: c.revenue, share: c.share }))}
                  emptyLabel="No customer revenue this month."
                />
              </Card>
            </Reveal>

            <Reveal delayMs={380}>
              <Card className="h-full">
                <CardHeader
                  title="Biggest suppliers"
                  description="All time — a buying relationship is built over years, not in a month."
                />
                <RankedBars
                  rows={a.topSuppliers.map((sup) => ({ label: sup.name, value: sup.spend, share: sup.share }))}
                  emptyLabel="No supplier purchases recorded."
                />
              </Card>
            </Reveal>
          </div>

          {/*
            Full width, and a column per fact rather than a name squeezed against a figure. These are the
            longest names in the system — "Brows Engineering & Constructions (Pvt) Ltd." — and at half
            width they truncated to the point where two customers could look like the same one.
          */}
          <Reveal delayMs={440}>
            <Card>
              <CardHeader
                title="Customers who have gone quiet"
                description={
                  a.lapsedCount === 0
                    ? "Nobody has stopped buying."
                    : `${a.lapsedCount} have not bought in 90 days — ${formatMoney(a.lapsedValue)} of past business.`
                }
              />

              {a.lapsedCustomers.length === 0 ? (
                <div className="flex h-32 items-center justify-center rounded-lg border border-dashed border-subtle text-sm text-muted">
                  Every customer has bought recently.
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-subtle text-left">
                        <th className="pb-2 pr-4 font-medium text-muted">Customer</th>
                        <th className="pb-2 pr-4 font-medium text-muted">Last bought</th>
                        <th className="pb-2 pr-4 text-right font-medium text-muted">Silent</th>
                        <th className="pb-2 pr-4 text-right font-medium text-muted">Lifetime value</th>
                        <th className="pb-2 text-right font-medium text-muted">Still owed</th>
                      </tr>
                    </thead>
                    <tbody>
                      {a.lapsedCustomers.map((c) => (
                        <tr key={c.name} className="border-b border-subtle/60 last:border-0">
                          <td className="py-2.5 pr-4 text-text">{c.name}</td>
                          <td className="whitespace-nowrap py-2.5 pr-4 text-muted">{formatReportDate(c.lastPurchase)}</td>
                          <td className="tabular whitespace-nowrap py-2.5 pr-4 text-right text-muted">{c.silentDays} days</td>
                          <td className="tabular py-2.5 pr-4 text-right text-text">{formatMoney(c.lifetime)}</td>
                          {/* Gone and still owing is a different problem from merely gone, so it is its
                              own column rather than a note tucked beside the name. */}
                          <td className={c.stillOwed > 0 ? "tabular py-2.5 text-right font-medium text-warning-text" : "tabular py-2.5 text-right text-muted"}>
                            {c.stillOwed > 0 ? formatMoney(c.stillOwed) : "—"}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </Reveal>
        </>
      ) : null}
    </FadeIn>
  );
}

/**
 * What the cash chart adds up to over its window.
 *
 * The chart shows each month against the last; this answers the question the chart is there to raise —
 * over six months, did more come in than went out. The net is the figure that matters and the one the
 * bars cannot show, because it is the accumulation of the gaps between them rather than any single bar.
 *
 * The count of cash-negative months sits underneath it because the net alone can hide them: five good
 * months and one catastrophic one nets out positive and still means a month the wages were hard to pay.
 *
 * Derived here rather than on the server — it is a sum of figures already on the page, and a round trip
 * to add up six numbers the client is holding would be a request for nothing.
 */
function CashSummary({ points }: { points: { in: number; out: number }[] }) {
  if (points.length === 0) return null;

  const received = points.reduce((sum, p) => sum + p.in, 0);
  const paidOut = points.reduce((sum, p) => sum + p.out, 0);
  const net = received - paidOut;
  const negativeMonths = points.filter((p) => p.out > p.in).length;

  return (
    <div className="mt-5 border-t border-subtle pt-4">
      <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-muted">
        Across these {points.length} months
      </p>

      <dl className="space-y-1.5 text-sm">
        <div className="flex items-baseline justify-between gap-3">
          <dt className="text-muted">Received</dt>
          <dd className="tabular text-text">{formatMoney(received)}</dd>
        </div>
        <div className="flex items-baseline justify-between gap-3">
          <dt className="text-muted">Paid out</dt>
          <dd className="tabular text-text">{formatMoney(paidOut)}</dd>
        </div>
        <div className="flex items-baseline justify-between gap-3 border-t border-subtle pt-1.5">
          <dt className="font-medium text-text">Net</dt>
          {/* Wording carries the meaning; colour only agrees with it. */}
          <dd className={net >= 0 ? "tabular font-medium text-success-text" : "tabular font-medium text-warning-text"}>
            {net >= 0 ? "+" : "−"}{formatMoney(Math.abs(net))}
          </dd>
        </div>
      </dl>

      <p className="mt-3 text-xs text-muted">
        {negativeMonths === 0
          ? "Every month took in more than it paid out."
          : `${negativeMonths} of these ${points.length} months paid out more than came in.`}
      </p>
    </div>
  );
}

/**
 * A panel that rises into place, staggered behind the ones above it.
 *
 * The stagger is the point: the eye is led down the screen in the order the questions are meant to be
 * read — headline, then trend, then the two panels that need acting on — instead of six cards appearing
 * at once and competing. `fill-mode-backwards` keeps the panel invisible during its delay rather than
 * flashing in and then animating.
 *
 * Motion preference is honoured by the utility itself (`motion-safe:`), so a reduced-motion setting
 * gets the finished layout immediately with nothing moving.
 */
function Reveal({ delayMs, children }: { delayMs: number; children: React.ReactNode }) {
  return (
    <div
      className="motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-bottom-4 motion-safe:fill-mode-backwards motion-safe:duration-500 motion-safe:ease-out"
      style={{ animationDelay: `${delayMs}ms` }}
    >
      {children}
    </div>
  );
}
