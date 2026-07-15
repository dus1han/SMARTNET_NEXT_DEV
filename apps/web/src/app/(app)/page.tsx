"use client";

import { useQuery } from "@tanstack/react-query";
import {
  ArrowRight,
  KeyRound,
  ScrollText,
  Settings as SettingsIcon,
  ShieldCheck,
  Users as UsersIcon,
  type LucideIcon,
} from "lucide-react";
import Link from "next/link";
import { me } from "@/lib/auth";
import { listCompanies } from "@/lib/settings";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/shell/app-shell";
import { Badge, Card } from "@/components/ui";

/**
 * The landing page.
 *
 * It is deliberately NOT the real dashboard — that is Phase 4, and it needs reports and charts that
 * do not exist yet. Inventing plausible sales figures to make the screen look busy would be worse
 * than an empty one: somebody would eventually believe them, and this is an accounting system.
 *
 * So it shows what is actually true today: who you are, what you may reach, and what the rebuild
 * has already fixed.
 */
export default function HomePage() {
  const { data: user } = useQuery({ queryKey: ["me"], queryFn: me });
  const { data: companies } = useQuery({ queryKey: ["companies"], queryFn: listCompanies });

  const permissions = user?.permissions ?? [];

  return (
    <>
      <PageHeader
        title={`Welcome back, ${user?.username ?? ""}`}
        description="Authentication, roles, auditing and settings are live."
      />

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat
          icon={ShieldCheck}
          label="Your permissions"
          value={String(permissions.length)}
          hint="Enforced on every request"
        />
        <Stat
          icon={SettingsIcon}
          label="Companies"
          value={companies ? String(companies.length) : "—"}
          hint="Scoped by the switcher above"
        />
        <Stat
          icon={KeyRound}
          label="Passwords"
          value="Argon2id"
          hint="Was: 4-char plaintext"
          tone="success"
        />
        <Stat
          icon={ScrollText}
          label="Every change"
          value="Audited"
          hint="Who, when and why"
          tone="success"
        />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <h2 className="font-medium text-text">What the rebuild has closed</h2>
          <p className="mt-1 text-sm text-muted">Each of these was a live defect in the old system.</p>

          <ul className="mt-5 space-y-4">
            {CLOSED.map((entry) => (
              <li key={entry.id} className="flex gap-3">
                <Badge tone="success" className="mt-0.5 shrink-0 font-mono">
                  {entry.id}
                </Badge>

                <div className="min-w-0">
                  <p className="text-sm font-medium text-text">{entry.title}</p>
                  <p className="mt-0.5 text-sm text-muted">{entry.detail}</p>
                </div>
              </li>
            ))}
          </ul>
        </Card>

        <Card>
          <h2 className="font-medium text-text">Jump to</h2>
          <p className="mt-1 text-sm text-muted">Only what your permissions allow.</p>

          <div className="mt-5 space-y-2">
            {permissions.includes("users") && (
              <Shortcut href="/users" icon={UsersIcon} label="Manage users" />
            )}

            {permissions.includes("settings.manage") && (
              <Shortcut href="/settings" icon={SettingsIcon} label="Settings" />
            )}

            {!permissions.includes("users") && !permissions.includes("settings.manage") && (
              <p className="text-sm text-muted">
                Your account holds no administrative permissions. The modules you do have arrive from
                Phase 3 onward.
              </p>
            )}
          </div>
        </Card>
      </div>
    </>
  );
}

const CLOSED = [
  {
    id: "A4",
    title: "Plaintext passwords",
    detail:
      "Both accounts were four characters, stored in clear text. Now Argon2id, upgraded silently on first login.",
  },
  {
    id: "A5",
    title: "Cosmetic authorization",
    detail:
      "Any logged-in user could call the admin endpoints. Now denied by default, checked per permission.",
  },
  {
    id: "A2",
    title: "Hardcoded SMTP password",
    detail: "Shipped in the source code. Now encrypted at rest, write-only, with a send kill switch.",
  },
  {
    id: "B4",
    title: "Duplicate document numbers",
    detail:
      "Two quotations already share the number STQ-0. Numbers are now unique-indexed and allocated under a lock.",
  },
  {
    id: "F2",
    title: "No audit trail",
    detail: "Updates and deletes recorded nothing at all. Every change now carries who, when and why.",
  },
];

function Stat({ icon: Icon, label, value, hint, tone }: {
  icon: LucideIcon;
  label: string;
  value: string;
  hint: string;
  tone?: "success";
}) {
  return (
    <Card className="relative overflow-hidden">
      {/* A whisper of the accent behind the corner, so a row of cards reads with some depth rather
          than as four grey boxes. Decorative, and hidden from assistive tech. */}
      <div
        aria-hidden
        className={cn(
          "pointer-events-none absolute -right-6 -top-6 size-20 rounded-full blur-2xl",
          tone === "success" ? "bg-success/20" : "bg-primary/20",
        )}
      />

      <div className="flex items-center gap-2">
        <Icon
          className={cn("size-4", tone === "success" ? "text-success" : "text-primary")}
          aria-hidden
        />
        <p className="text-sm font-medium text-muted">{label}</p>
      </div>

      <p className="tabular mt-3 text-2xl font-semibold tracking-tight text-text">{value}</p>
      <p className="mt-1 text-xs text-muted">{hint}</p>
    </Card>
  );
}

function Shortcut({ href, icon: Icon, label }: { href: string; icon: LucideIcon; label: string }) {
  return (
    <Link
      href={href}
      className={cn(
        "group flex items-center gap-3 rounded-lg border border-subtle px-3 py-2.5 text-sm font-medium text-text",
        "transition-colors duration-200 hover:border-strong hover:bg-surface-sunken",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/30",
      )}
    >
      <span className="grid size-8 place-items-center rounded-lg bg-primary-ghost text-primary">
        <Icon className="size-4" aria-hidden />
      </span>

      <span className="flex-1">{label}</span>

      <ArrowRight
        className="size-4 text-muted transition-transform duration-200 group-hover:translate-x-0.5"
        aria-hidden
      />
    </Link>
  );
}
