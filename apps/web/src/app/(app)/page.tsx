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
import { getDashboard, type CompanyFilter } from "@/lib/dashboard";
import { PageHeader } from "@/components/shell/app-shell";
import { DailySalesChart } from "@/components/dashboard/daily-sales-chart";
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
        </>
      ) : null}
    </FadeIn>
  );
}

function periodLabel(start: string, end: string): string {
  const from = new Date(`${start}T00:00:00`);
  if (Number.isNaN(from.getTime())) return `${start} – ${end}`;
  return from.toLocaleDateString(undefined, { month: "long", year: "numeric" });
}
