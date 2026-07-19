"use client";

/**
 * The dashboard route — one address, two dashboards, chosen by permission.
 *
 * A user holds exactly one of `dashboard` (management) or `dashboard.operations`, enforced when their
 * permissions are saved. So this decides which to render rather than offering a choice: the decision was
 * made when the account was set up, and a landing page is not somewhere to ask.
 *
 * One route rather than two addresses, so nobody has to know which dashboard they are entitled to in
 * order to find it — and a bookmark keeps working if their permissions change.
 */

import { useQuery } from "@tanstack/react-query";
import { me } from "@/lib/auth";
import { ManagementDashboard } from "@/components/dashboard/management-dashboard";
import { OperationsDashboard } from "@/components/dashboard/operations-dashboard";
import { Card, FadeIn, LoadingPanel } from "@/components/ui";

export default function DashboardPage() {
  const user = useQuery({ queryKey: ["me"], queryFn: me });

  if (user.isPending) return <LoadingPanel />;

  const permissions = user.data?.permissions ?? [];

  // Management wins if somebody holds both. That should be impossible — the save refuses it — but if a
  // legacy row or a role grant ever produces the pair, showing the fuller view is the safer failure: it
  // is visible and obviously wrong, where silently withholding the figures from an owner looks like the
  // data is missing.
  if (permissions.includes("dashboard") || permissions.includes("system.dev_admin")) {
    return <ManagementDashboard />;
  }

  if (permissions.includes("dashboard.operations")) {
    return <OperationsDashboard />;
  }

  return (
    <FadeIn>
      <Card className="p-8 text-center">
        <p className="font-medium text-text">No dashboard is assigned to your account.</p>
        <p className="mt-2 text-sm text-muted">
          Every user gets one of the two — the management dashboard or the operations one. Ask an
          administrator to assign yours.
        </p>
      </Card>
    </FadeIn>
  );
}
