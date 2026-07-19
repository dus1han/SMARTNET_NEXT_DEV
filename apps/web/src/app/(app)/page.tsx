"use client";

/**
 * The dashboard — what the business looks like, not what it totalled.
 *
 * Six readings, each one answering a question somebody actually asks: which way are we going, where has
 * the money stopped moving, is cash keeping up with profit, who are we dependent on, what sells. The
 * earlier month-at-a-glance tiles and the daily-sales bars are gone — they showed a total with no
 * direction and an "outstanding" figure that concealed the ageing behind it.
 *
 * One company filter above everything, in a row of its own: the same control the reports use, so
 * switching company means the same thing on every screen.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, Coins, TrendingUp, Wallet } from "lucide-react";
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
import { ReportFilterBar, StatTile, formatMoney } from "@/components/reports";
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

      <PageHeader title="Dashboard" description="How the business is doing, and where it needs attention." />

      <ReportFilterBar company={company} onCompany={setCompany} showDates={false} />

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

          <Reveal delayMs={120}>
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
              </Card>
            </Reveal>

            <Reveal delayMs={260}>
              <Card className="h-full">
                <CardHeader
                  title="Cash in and out"
                  description="Receipts against supplier payments and expenses. Profit is not cash."
                />
                <CashFlowChart points={a.cashFlow} />
              </Card>
            </Reveal>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Reveal delayMs={320}>
              <Card className="h-full">
                <CardHeader
                  title="Biggest customers"
                  description={`The top five are ${a.topCustomerShare.toFixed(1)}% of all revenue.`}
                />
                <RankedBars
                  rows={a.topCustomers.map((c) => ({ label: c.name, value: c.revenue, share: c.share }))}
                  emptyLabel="No customer revenue yet."
                />
              </Card>
            </Reveal>

            <Reveal delayMs={380}>
              <Card className="h-full">
                <CardHeader
                  title="Best-selling lines"
                  description="By revenue, with units alongside — the legacy data holds no per-line cost, so there is no item margin to show."
                />
                <RankedBars
                  rows={a.topItems.map((i) => ({
                    label: i.description,
                    value: i.revenue,
                    share: i.share,
                    note: `${i.quantity.toLocaleString()} units`,
                  }))}
                  emptyLabel="No item sales yet."
                />
              </Card>
            </Reveal>
          </div>
        </>
      ) : null}
    </FadeIn>
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
