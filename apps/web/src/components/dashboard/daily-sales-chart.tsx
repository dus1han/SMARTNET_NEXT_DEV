"use client";

/**
 * Daily cash-vs-credit sales for the month — hand-rolled SVG, no charting dependency.
 *
 * The dashboard needs two chart shapes, not a BI suite, so a heavy framework would be all cost and no
 * benefit (and the app's Next build has its own opinions about client libraries). This is a stacked
 * bar per day: cash on aqua, credit on blue — two categorical hues validated for colour-blind
 * separation (ΔE ≫ 12) and for contrast in both themes. Identity is never colour alone: the legend
 * names each series, and the hover tooltip reads out the figures.
 */

import { useState } from "react";
import type { DailySalesPoint } from "@/lib/dashboard";
import { formatMoney } from "@/components/reports";

const PAD = { top: 12, right: 12, bottom: 28, left: 48 };
const HEIGHT = 240;

export function DailySalesChart({ points }: { points: DailySalesPoint[] }) {
  const [hovered, setHovered] = useState<number | null>(null);

  const maxTotal = Math.max(0, ...points.map((p) => p.cash + p.credit));

  if (points.length === 0 || maxTotal === 0) {
    return (
      <div className="flex h-48 items-center justify-center rounded-lg border border-dashed border-subtle text-sm text-muted">
        No sales recorded this month yet.
      </div>
    );
  }

  const days = points.length;
  const width = Math.max(360, days * 22);
  const plotW = width - PAD.left - PAD.right;
  const plotH = HEIGHT - PAD.top - PAD.bottom;

  const niceMax = niceCeil(maxTotal);
  const band = plotW / days;
  const barW = Math.min(band * 0.62, 18);
  const y = (value: number) => PAD.top + plotH - (value / niceMax) * plotH;
  const cx = (index: number) => PAD.left + band * index + band / 2;

  const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => niceMax * f);
  const labelStep = Math.ceil(days / 10);
  const active = hovered !== null ? points[hovered] : null;

  return (
    <div className="viz-daily-sales relative w-full">
      <style>{VIZ_CSS}</style>

      {/* Legend — always present for two series, so identity is never colour alone. */}
      <div className="mb-2 flex items-center gap-4 text-xs text-muted">
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm" style={{ background: "var(--cash)" }} aria-hidden />
          Cash
        </span>
        <span className="inline-flex items-center gap-1.5">
          <span className="size-2.5 rounded-sm" style={{ background: "var(--credit)" }} aria-hidden />
          Credit
        </span>
      </div>

      <svg
        viewBox={`0 0 ${width} ${HEIGHT}`}
        className="w-full"
        height={HEIGHT}
        role="img"
        aria-label={`Daily cash and credit sales. Peak day total ${formatMoney(maxTotal)}.`}
        preserveAspectRatio="none"
      >
        <defs>
          {/* A soft vertical sheen on each bar — lighter at the top — so the fills read with depth
              rather than as flat blocks. Same hue as the legend swatch; the opacity does the work. */}
          <linearGradient id="grad-cash" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--cash)" stopOpacity="0.82" />
            <stop offset="100%" stopColor="var(--cash)" stopOpacity="1" />
          </linearGradient>
          <linearGradient id="grad-credit" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--credit)" stopOpacity="0.82" />
            <stop offset="100%" stopColor="var(--credit)" stopOpacity="1" />
          </linearGradient>
        </defs>

        {/* Gridlines + y-axis ticks — recessive. */}
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PAD.left} x2={width - PAD.right} y1={y(t)} y2={y(t)} className="viz-grid" />
            <text x={PAD.left - 8} y={y(t) + 3} textAnchor="end" className="viz-axis">
              {formatCompact(t)}
            </text>
          </g>
        ))}

        {points.map((p, i) => {
          const cashH = PAD.top + plotH - y(p.cash);
          const creditH = PAD.top + plotH - y(p.credit);
          const hasCredit = p.credit > 0;
          const hasCash = p.cash > 0;
          const x = cx(i) - barW / 2;

          return (
            <g key={p.date}>
              {/* The bars grow up from the baseline on arrival, lightly staggered across the month. */}
              <g
                className="animate-rise"
                style={{
                  transformBox: "fill-box",
                  transformOrigin: "bottom",
                  animationDelay: `${Math.min(i, 30) * 12}ms`,
                }}
                opacity={hovered === null || hovered === i ? 1 : 0.5}
              >
                {hasCash && (
                  <rect
                    x={x}
                    y={PAD.top + plotH - cashH}
                    width={barW}
                    height={cashH}
                    rx={hasCredit ? 0 : 3}
                    fill="url(#grad-cash)"
                  />
                )}
                {hasCredit && (
                  // Stacked above cash, with a 2px surface gap between the fills.
                  <rect
                    x={x}
                    y={PAD.top + plotH - cashH - 2 - creditH}
                    width={barW}
                    height={creditH}
                    rx={3}
                    fill="url(#grad-credit)"
                  />
                )}
              </g>

              {/* A wide, invisible hit target — bigger than the bar, per the interaction spec. */}
              <rect
                x={PAD.left + band * i}
                y={PAD.top}
                width={band}
                height={plotH}
                fill="transparent"
                onMouseEnter={() => setHovered(i)}
                onMouseLeave={() => setHovered(null)}
              />
            </g>
          );
        })}

        {/* Baseline. */}
        <line
          x1={PAD.left}
          x2={width - PAD.right}
          y1={PAD.top + plotH}
          y2={PAD.top + plotH}
          className="viz-baseline"
        />

        {/* X labels — a subset, so 31 days do not collide. */}
        {points.map((p, i) =>
          i % labelStep === 0 ? (
            <text key={`x-${p.date}`} x={cx(i)} y={HEIGHT - 10} textAnchor="middle" className="viz-axis">
              {dayOfMonth(p.date)}
            </text>
          ) : null,
        )}
      </svg>

      {active && hovered !== null && (
        <div
          className="pointer-events-none absolute top-6 z-10 -translate-x-1/2 rounded-lg border border-subtle bg-surface px-3 py-2 text-xs shadow-lg"
          style={{ left: `${(cx(hovered) / width) * 100}%` }}
        >
          <p className="font-medium text-text">{fullDate(active.date)}</p>
          <p className="mt-1 flex items-center justify-between gap-4">
            <span className="text-muted">Cash</span>
            <span className="tabular text-text">{formatMoney(active.cash)}</span>
          </p>
          <p className="flex items-center justify-between gap-4">
            <span className="text-muted">Credit</span>
            <span className="tabular text-text">{formatMoney(active.credit)}</span>
          </p>
          <p className="mt-0.5 flex items-center justify-between gap-4 border-t border-subtle pt-0.5">
            <span className="text-muted">Total</span>
            <span className="tabular font-medium text-text">{formatMoney(active.cash + active.credit)}</span>
          </p>
        </div>
      )}
    </div>
  );
}

// Cash = emerald, credit = indigo — the same hue family as the pastel KPI tiles, so the dashboard reads
// as one palette. Validated for colour-blind separation (ΔE ≫ 12) and in-band lightness on each
// surface; emerald's sub-3:1 contrast on the light ground is covered by the legend, the hover read-out
// and the numeric KPIs (the relief rule). The dark steps win under both OS-dark and the theme toggle.
const VIZ_CSS = `
.viz-daily-sales { --cash: #10b981; --credit: #6366f1; }
.viz-daily-sales .viz-grid { stroke: #e1e0d9; stroke-width: 1; }
.viz-daily-sales .viz-baseline { stroke: #c3c2b7; stroke-width: 1; }
.viz-daily-sales .viz-axis { fill: #898781; font-size: 10px; }
@media (prefers-color-scheme: dark) {
  :root:where(:not([data-theme="light"])) .viz-daily-sales { --cash: #0d9f70; --credit: #6366f1; }
  :root:where(:not([data-theme="light"])) .viz-daily-sales .viz-grid { stroke: #2c2c2a; }
  :root:where(:not([data-theme="light"])) .viz-daily-sales .viz-baseline { stroke: #383835; }
}
:root[data-theme="dark"] .viz-daily-sales { --cash: #0d9f70; --credit: #6366f1; }
:root[data-theme="dark"] .viz-daily-sales .viz-grid { stroke: #2c2c2a; }
:root[data-theme="dark"] .viz-daily-sales .viz-baseline { stroke: #383835; }
`;

function niceCeil(value: number): number {
  if (value <= 0) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  return Math.ceil(value / magnitude) * magnitude;
}

function formatCompact(value: number): string {
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1).replace(/\.0$/, "")}M`;
  if (value >= 1_000) return `${(value / 1_000).toFixed(1).replace(/\.0$/, "")}k`;
  return String(Math.round(value));
}

function dayOfMonth(iso: string): string {
  return iso.slice(8, 10).replace(/^0/, "");
}

function fullDate(iso: string): string {
  const date = new Date(`${iso}T00:00:00`);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleDateString(undefined, { dateStyle: "medium" });
}
