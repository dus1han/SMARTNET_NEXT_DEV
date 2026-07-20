"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";
import { BRAND_NAME, BrandMark } from "./brand";
import { visibleSections } from "./nav";

/**
 * The sidebar. Dark in both themes — it frames the content and gives the eye an anchor.
 *
 * Its contents come from the user's permission claims, so a storeman does not see an Accounts
 * menu. That is presentation, not protection: the API denies by default and re-checks on every
 * request (ISSUES A5).
 */
export function Sidebar({ permissions, onNavigate }: {
  permissions: string[];
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const sections = visibleSections(permissions);

  // Exactly one item is active: the one whose href is the *longest* segment-prefix of the current path.
  // Without the "longest" rule, `/invoices` would also light up on `/invoices/deleted` (a startsWith
  // match on the parent), so both the Invoices and Deleted-invoices items would highlight at once.
  const matchesPath = (href: string) =>
    href === "/" ? pathname === "/" : pathname === href || pathname.startsWith(`${href}/`);
  const activeHref = sections
    .flatMap((section) => section.items)
    .filter((item) => !item.phase && matchesPath(item.href))
    .map((item) => item.href)
    .sort((a, b) => b.length - a.length)[0];

  return (
    <nav
      aria-label="Main"
      className="flex h-full w-64 shrink-0 flex-col border-r border-sidebar-border bg-sidebar"
    >
      {/*
        The brand block, and it is a link home — a logo in an app shell that does nothing is a
        missed affordance, and every user already expects it to go to the dashboard.

        MOTION, DELIBERATELY UNLIKE THE SIGN-IN SCREEN. The login mark floats, haloes and ripples
        forever, which is right for a panel looked at for four seconds. This one is on screen all
        day beside people typing invoices, and the house rule past that door is that motion is
        "functional and short". So: it arrives once on mount, and it responds when pointed at.
        Nothing loops. A logo breathing in the corner of the eye for eight hours is not charm, it
        is a thing people learn to resent — and `prefers-reduced-motion` removes even this, by the
        global rule in globals.css.
      */}
      <Link
        href="/"
        onClick={onNavigate}
        aria-label={`${BRAND_NAME} — go to the dashboard`}
        className={cn(
          "group/brand flex h-16 items-center gap-2.5 border-b border-sidebar-border px-5",
          "transition-colors duration-200 ease-out hover:bg-sidebar-active/40",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary/50",
        )}
      >
        <span className="relative overflow-hidden rounded-lg animate-in fade-in-0 zoom-in-90 duration-500 ease-out">
          <BrandMark
            className="size-8 rounded-lg transition-transform duration-200 ease-out group-hover/brand:scale-105"
          />
          {/* The sheen sweeps across the mark on hover only — the same gesture the sign-in button
              uses, so the two surfaces share a vocabulary without sharing a loop. */}
          <span
            aria-hidden
            className={cn(
              "pointer-events-none absolute inset-0 bg-gradient-to-r from-transparent via-white/40 to-transparent",
              "-translate-x-[120%] skew-x-[-12deg] opacity-0",
              "group-hover/brand:opacity-100 group-hover/brand:animate-sheen",
            )}
          />
        </span>

        {/* Name only. The tagline belongs on the sign-in screen, which is selling the product; by
            the time somebody is looking at this header they are using it. */}
        <p
          className={cn(
            "text-sm font-semibold text-sidebar-text-active",
            "animate-in fade-in-0 slide-in-from-left-2 fill-mode-backwards duration-500 ease-out [animation-delay:90ms]",
          )}
        >
          {BRAND_NAME}
        </p>
      </Link>

      <div className="flex-1 space-y-6 overflow-y-auto px-3 py-5">
        {sections.map((section) => (
          <div key={section.title}>
            <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wider text-sidebar-text/60">
              {section.title}
            </p>

            <ul className="space-y-0.5">
              {section.items.map((item) => {
                const active = item.href === activeHref;

                // Not built yet. Shown, but plainly not clickable — so the shape of the finished
                // app is visible from day one, and nobody clicks a link that goes nowhere.
                if (item.phase) {
                  return (
                    <li key={item.href}>
                      <span
                        className="flex cursor-not-allowed items-center gap-3 rounded-lg px-3 py-2 text-sm text-sidebar-text/40"
                        title={`Arrives in ${item.phase}`}
                      >
                        <item.icon className="size-4 shrink-0" aria-hidden />
                        <span className="flex-1 truncate">{item.label}</span>
                        <span className="rounded bg-sidebar-active/50 px-1.5 py-0.5 text-[10px] font-medium">
                          {item.phase.replace("Phase ", "P")}
                        </span>
                      </span>
                    </li>
                  );
                }

                return (
                  <li key={item.href}>
                    <Link
                      href={item.href}
                      onClick={onNavigate}
                      aria-current={active ? "page" : undefined}
                      className={cn(
                        "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium",
                        "transition-colors duration-200 ease-out",
                        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50",
                        active
                          ? "bg-sidebar-active text-sidebar-text-active"
                          : "text-sidebar-text hover:bg-sidebar-active/50 hover:text-sidebar-text-active",
                      )}
                    >
                      <item.icon className="size-4 shrink-0" aria-hidden />
                      <span className="truncate">{item.label}</span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          </div>
        ))}
      </div>

    </nav>
  );
}
