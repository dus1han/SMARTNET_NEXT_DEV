"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";
import { BRAND_NAME, BRAND_TAGLINE, BrandMark } from "./brand";
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

  return (
    <nav
      aria-label="Main"
      className="flex h-full w-64 shrink-0 flex-col border-r border-sidebar-border bg-sidebar"
    >
      <div className="flex h-16 items-center gap-2.5 border-b border-sidebar-border px-5">
        <BrandMark className="size-8 rounded-lg" />
        <div className="leading-tight">
          <p className="text-sm font-semibold text-sidebar-text-active">{BRAND_NAME}</p>
          <p className="text-[11px] text-sidebar-text">{BRAND_TAGLINE}</p>
        </div>
      </div>

      <div className="flex-1 space-y-6 overflow-y-auto px-3 py-5">
        {sections.map((section) => (
          <div key={section.title}>
            <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wider text-sidebar-text/60">
              {section.title}
            </p>

            <ul className="space-y-0.5">
              {section.items.map((item) => {
                const active =
                  item.href === "/" ? pathname === "/" : pathname.startsWith(item.href);

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

      <div className="border-t border-sidebar-border px-5 py-3">
        <p className="text-[11px] text-sidebar-text/50">Phase 2 · design system</p>
      </div>
    </nav>
  );
}
