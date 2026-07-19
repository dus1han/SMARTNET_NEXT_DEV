"use client";

/**
 * The dashboard — Phase 4, slice 2. One screen, two shapes.
 *
 * The server chooses the shape from the token: a user who holds `dashboard` sees the company view
 * (every invoice in the company they are working in); everyone else sees the "my" view, scoped to what
 * they prepared. That scoping is approximate until Phase 5 — it joins on the legacy `preparedby` name
 * string, which a rename breaks — and it says so. The three legacy dashboard controllers (Admin, User,
 * and the dropped Customer one) collapse to this.
 */

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { AlertTriangle, Building2, Coins, TrendingUp, UserRound, Wallet } from "lucide-react";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { getDashboard, getDashboardAnalytics, type CompanyFilter } from "@/lib/dashboard";
import { PageHeader } from "@/components/shell/app-shell";
import { DailySalesChart } from "@/components/dashboard/daily-sales-chart";
import {
  ANALYTICS_VIZ_CSS,
  AgeingChart,
  CashFlowChart,
  Delta,
  MonthlyTrendChart,
  RankedBars,
} from "@/components/dashboard/analytics-charts";
import { CompanyFilterCard } from "@/components/dashboard/company-filter-card";
import { StatTile, formatMoney } from "@/components/reports";
import { AnimatedNumber, Badge, Card, CardHeader, ErrorBanner, FadeIn, LoadingPanel } from "@/components/ui";

export default function DashboardPage() {
  const [company, setCompany] = useState<CompanyFilter>("all");

  const dashboard = useQuery({
    queryKey: ["dashboard", company],
    queryFn: () => getDashboard(company),
    // Keep the current figures on screen while a company switch loads, so the tiles animate to the new
    // numbers rather than collapsing to a spinner and back.
    placeholderData: keepPreviousData,
  });

  const loadError = dashboard.error as ApiError | null;
  const data = dashboard.data;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Dashboard"
        description={data ? periodLabel(data.periodStart, data.periodEnd) : "This month at a glance."}
        actions={
          data?.view === "my" ? (
            <Badge
              tone="warning"
              title="Scoped to documents prepared under your name. Until Phase 5, that is a name match on the legacy data — approximate."
            >
              Your sales · approximate
            </Badge>
          ) : undefined
        }
      />

      {loadError && <ErrorBanner message={loadError.message} correlationId={loadError.correlationId} />}

      {dashboard.isPending ? (
        <LoadingPanel />
      ) : data ? (
        <>
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Sales this month"
              icon={TrendingUp}
              color="indigo"
              delayMs={0}
              value={<AnimatedNumber value={data.totalSales} format={formatMoney} />}
              sub={`Cash ${formatMoney(data.cashSales)} · Credit ${formatMoney(data.creditSales)}`}
            />
            <StatTile
              label="Profit this month"
              icon={Coins}
              color="emerald"
              delayMs={70}
              value={<AnimatedNumber value={data.profit} format={formatMoney} />}
            />
            <StatTile
              label="Outstanding"
              icon={Wallet}
              color="amber"
              delayMs={140}
              value={<AnimatedNumber value={data.outstanding} format={formatMoney} />}
            />
            {data.companies.length >= 2 ? (
              <CompanyFilterCard
                companies={data.companies}
                selected={company}
                onChange={setCompany}
                delayMs={210}
              />
            ) : (
              <StatTile
                label={data.view === "my" ? "Your invoices" : "Company"}
                icon={data.view === "my" ? UserRound : Building2}
                color="violet"
                delayMs={210}
                value={data.view === "my" ? "Yours only" : "All"}
              />
            )}
          </div>

          {data.flaggedCount > 0 && (
            <p className="flex items-center gap-2 text-sm text-warning-text">
              <AlertTriangle className="size-4" aria-hidden />
              {data.flaggedCount} invoice{data.flaggedCount === 1 ? "" : "s"} this month carry a value
              we could not read from the legacy data. It is counted as zero.
            </p>
          )}

          <Card>
            <CardHeader
              title="Daily sales"
              description="Cash and credit for each day of the month, for the company you are working in."
            />
            <DailySalesChart points={data.chart} />
          </Card>

          <Analytics company={company} />
        </>
      ) : null}
    </FadeIn>
  );
}

/**
 * The analytical half — loaded separately so the month tiles above never wait on it.
 *
 * Its own component, and its own query, for that reason: this scans a year of invoices and their lines
 * while the tiles above scan one month, so sharing a request would hold the whole screen behind the
 * slower half.
 */
function Analytics({ company }: { company: CompanyFilter }) {
  const analytics = useQuery({
    queryKey: ["dashboard-analytics", company],
    queryFn: () => getDashboardAnalytics(company),
    placeholderData: keepPreviousData,
  });

  const a = analytics.data;

  if (analytics.isPending) return <LoadingPanel />;
  if (!a) return null;

  const overdueShare = a.ageing.reduce((sum, b) => sum + b.amount, 0);

  return (
    <>
      <style>{ANALYTICS_VIZ_CSS}</style>

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
          sub={<><span className="tabular">{a.marginPercent.toFixed(1)}% margin</span> · <Delta change={a.grossProfit.changePercent} /></>}
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
          sub={
            overdueShare > 0
              ? `${Math.round((a.overdue / overdueShare) * 100)}% of everything owed`
              : "nothing owed"
          }
        />
      </div>

      <Card>
        <CardHeader
          title="Revenue and profit"
          description="Twelve months. The gap between the pair is what the sales cost you."
        />
        <MonthlyTrendChart points={a.monthlyTrend} />
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader
            title="Where the money is owed"
            description="Open balances by age. Anything past the first bar is money that has stopped moving."
          />
          <AgeingChart buckets={a.ageing} />
        </Card>

        <Card>
          <CardHeader
            title="Cash in and out"
            description="Receipts against supplier payments and expenses. Profit is not cash."
          />
          <CashFlowChart points={a.cashFlow} />
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader
            title="Biggest customers"
            description={`The top five are ${a.topCustomerShare.toFixed(1)}% of all revenue.`}
          />
          <RankedBars
            rows={a.topCustomers.map((c) => ({ label: c.name, value: c.revenue, share: c.share }))}
            emptyLabel="No customer revenue yet."
          />
        </Card>

        <Card>
          <CardHeader
            title="Best-selling lines"
            description="By revenue, with units alongside — the legacy data has no per-line cost, so there is no item margin to show."
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
      </div>
    </>
  );
}

function periodLabel(start: string, end: string): string {
  const from = new Date(`${start}T00:00:00`);
  if (Number.isNaN(from.getTime())) return `${start} – ${end}`;
  return from.toLocaleDateString(undefined, { month: "long", year: "numeric" });
}
