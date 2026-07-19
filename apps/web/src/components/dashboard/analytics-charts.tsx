"use client";

/**
 * The dashboard's analytical charts — hand-rolled SVG, no charting dependency.
 *
 * The dashboard needs a handful of shapes, not a BI suite, so a charting framework would be all cost and
 * no benefit. The conventions here — a scoped `viz-*` class carrying the hues as CSS variables so
 * dark mode is a *chosen* set of steps rather than an automatic flip, a legend whenever there is more
 * than one series, and a hover read-out so identity and value are never colour alone.
 *
 * **The palette is validated, not eyeballed.** Every pair below was run through the contrast/CVD checker:
 *   - revenue indigo `#4f46e5` vs profit emerald `#059669` — separable, both ≥ 3:1 on surface
 *   - money-in indigo vs money-out amber `#d97706` — ΔE 34 under protanopia, the strongest pair available
 *   - ageing is a single-hue amber ramp (sequential, light→dark) because the buckets are one quantity
 *     getting worse, not four categories
 *
 * Green-against-red is deliberately absent: it reads as ΔE 5.8 under deuteranopia — indistinguishable —
 * which is exactly the intuitive choice for "money in vs money out" that a checker catches and an eye
 * does not.
 */

import { useEffect, useRef, useState } from "react";
import type { AgeingBucket, CashFlowPoint, MonthPoint } from "@/lib/dashboard";
import { formatMoney } from "@/components/reports";

const AXIS_W = 52;

/**
 * The width the chart actually has, measured from its container.
 *
 * An SVG has to be told its width in numbers — it cannot lay itself out to a flex parent — so a chart
 * that hard-codes one either overflows a narrow card or, as these did, sits in a puddle inside a wide
 * one. Scaling a fixed viewBox to 100% would stretch the type along with the bars; measuring keeps the
 * labels at their real size and spends the extra room on the plot, which is the part that carries data.
 *
 * Falls back to a sensible width until the first measurement lands, so nothing jumps on load.
 */
function useMeasuredWidth(fallback: number) {
  const ref = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(fallback);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;

    const observer = new ResizeObserver(([entry]) => {
      // Round down: a fractional width leaves a sub-pixel gap that shows as a hairline on the right.
      const next = Math.floor(entry.contentRect.width);
      if (next > 0) setWidth(next);
    });

    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return [ref, width] as const;
}

/** Compact money for axes and dense labels — "1.2M", "450k" — so ticks do not collide. */
function compact(value: number): string {
  const abs = Math.abs(value);
  if (abs >= 1_000_000) return `${(value / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1)}M`;
  if (abs >= 1_000) return `${Math.round(value / 1_000)}k`;
  return Math.round(value).toString();
}

function niceCeil(value: number): number {
  if (value <= 0) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  return Math.ceil(value / magnitude) * magnitude;
}

function monthLabel(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { month: "short" });
}

function monthFull(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { month: "long", year: "numeric" });
}

function Empty({ children }: { children: string }) {
  return (
    <div className="flex h-48 items-center justify-center rounded-lg border border-dashed border-subtle text-sm text-muted">
      {children}
    </div>
  );
}

/** A legend swatch and label. Identity is never colour alone — this names the series. */
function Key({ colour, children }: { colour: string; children: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted">
      <span className="size-2.5 rounded-sm" style={{ background: colour }} aria-hidden />
      {children}
    </span>
  );
}

// --- Revenue and profit, twelve months ----------------------------------------------------------

/**
 * Grouped bars, one pair per month.
 *
 * Grouped rather than stacked: profit is *part of* revenue, and stacking them would read as a total of
 * the two, which is a number that does not exist. Side by side, the gap between the bars is the cost —
 * which is the thing worth looking at.
 *
 * One axis. Revenue and profit are both money at the same scale, so they share it honestly; a second
 * axis would let any two shapes be drawn to look correlated.
 */
export function MonthlyTrendChart({ points }: { points: MonthPoint[] }) {
  const [hovered, setHovered] = useState<number | null>(null);
  const [box, measured] = useMeasuredWidth(720);

  const max = Math.max(0, ...points.map((p) => Math.max(p.revenue, p.profit)));

  const H = 260;
  const PAD = { top: 12, right: 12, bottom: 30, left: AXIS_W };
  // Fills the card, but never squeezes below the point where the month labels would collide — past that
  // the container scrolls instead.
  const width = Math.max(points.length * 44, measured);
  const plotW = width - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;

  const top = niceCeil(max);
  const band = plotW / Math.max(1, points.length);
  const barW = Math.min(band * 0.32, 26);
  const y = (v: number) => PAD.top + plotH - (v / top) * plotH;

  return (
    <div className="viz-trend" ref={box}>
      {points.length === 0 || max === 0 ? (
        <Empty>No sales in the last twelve months.</Empty>
      ) : (
        <>
      <div className="mb-3 flex flex-wrap items-center gap-4">
        <Key colour="var(--revenue)">Revenue</Key>
        <Key colour="var(--profit)">Gross profit</Key>
      </div>

      <div className="overflow-x-auto">
        <svg width={width} height={H} role="img" aria-label="Revenue and gross profit by month">
          {[0, 0.25, 0.5, 0.75, 1].map((t) => (
            <g key={t}>
              <line className="viz-grid" x1={PAD.left} x2={width - PAD.right} y1={y(top * t)} y2={y(top * t)} />
              <text className="viz-axis" x={PAD.left - 8} y={y(top * t) + 3} textAnchor="end">{compact(top * t)}</text>
            </g>
          ))}

          {points.map((p, i) => {
            const cx = PAD.left + band * i + band / 2;
            const isHot = hovered === i;

            return (
              <g key={p.month}>
                {/* Hit target spans the whole band, so hovering never demands precision. */}
                <rect
                  x={PAD.left + band * i} y={PAD.top} width={band} height={plotH}
                  fill="transparent"
                  onMouseEnter={() => setHovered(i)}
                  onMouseLeave={() => setHovered(null)}
                />
                {isHot && <rect className="viz-hot" x={PAD.left + band * i} y={PAD.top} width={band} height={plotH} />}

                <rect
                  className="viz-bar" style={{ animationDelay: `${i * 40}ms` }}
                  x={cx - barW - 1} y={y(p.revenue)} width={barW} height={Math.max(0, plotH - (y(p.revenue) - PAD.top))}
                  rx={3} fill="var(--revenue)"
                />
                <rect
                  className="viz-bar" style={{ animationDelay: `${i * 40 + 20}ms` }}
                  x={cx + 1} y={y(p.profit)} width={barW} height={Math.max(0, plotH - (y(p.profit) - PAD.top))}
                  rx={3} fill="var(--profit)"
                />

                <text className="viz-axis" x={cx} y={H - 10} textAnchor="middle">{monthLabel(p.month)}</text>
              </g>
            );
          })}
        </svg>
      </div>

      {hovered !== null && points[hovered] && (
        <p className="mt-2 text-sm">
          <span className="font-medium text-text">{monthFull(points[hovered].month)}</span>
          <span className="text-muted"> · revenue </span>
          <span className="tabular text-text">{formatMoney(points[hovered].revenue)}</span>
          <span className="text-muted"> · profit </span>
          <span className="tabular text-text">{formatMoney(points[hovered].profit)}</span>
        </p>
      )}
        </>
      )}
    </div>
  );
}

// --- Receivables ageing -------------------------------------------------------------------------

/**
 * Horizontal bars, one per bucket, on a single-hue ramp that darkens with age.
 *
 * Horizontal because the labels are words ("Over 90 days"), and words read along a bar rather than
 * rotated under one. Sequential rather than categorical because these are not four kinds of thing —
 * they are one quantity, sorted by how overdue it is, and a ramp says that where four hues would not.
 *
 * Every bar is direct-labelled with its amount, which is also what discharges the sub-3:1 contrast on
 * the lightest step: the figure is readable whether or not the fill is.
 */
export function AgeingChart({ buckets }: { buckets: AgeingBucket[] }) {
  const total = buckets.reduce((sum, b) => sum + b.amount, 0);
  if (total === 0) return <Empty>Nothing outstanding.</Empty>;

  const max = Math.max(...buckets.map((b) => b.amount));

  return (
    <div className="viz-ageing space-y-2.5">
      {buckets.map((b, i) => (
        <div key={b.label} className="grid grid-cols-[104px_1fr_auto] items-center gap-3">
          <span className="text-xs text-muted">{b.label}</span>

          <div className="h-6 overflow-hidden rounded-sm bg-surface-sunken">
            <div
              className="viz-fill h-full rounded-sm"
              style={{
                width: `${Math.max(1, (b.amount / max) * 100)}%`,
                background: `var(--age-${i})`,
                animationDelay: `${i * 70}ms`,
              }}
              title={`${b.invoices} invoice${b.invoices === 1 ? "" : "s"}`}
            />
          </div>

          <span className="tabular text-right text-sm text-text">
            {formatMoney(b.amount)}
            <span className="ml-2 text-xs text-muted">{b.invoices}</span>
          </span>
        </div>
      ))}

      <p className="pt-1 text-xs text-muted">
        Aged from the invoice date — this data carries no due date. The right-hand figure is the invoice count.
      </p>
    </div>
  );
}

// --- Cash in and out ----------------------------------------------------------------------------

/**
 * Money received against money paid out, by month.
 *
 * Indigo and amber, not green and red. The obvious choice for in-versus-out fails deuteranopia at
 * ΔE 5.8 — the two bars become one colour for the reader most likely to be checking whether more went
 * out than came in.
 */
export function CashFlowChart({ points }: { points: CashFlowPoint[] }) {
  const [hovered, setHovered] = useState<number | null>(null);
  const [box, measured] = useMeasuredWidth(420);

  const max = Math.max(0, ...points.map((p) => Math.max(p.in, p.out)));

  const H = 220;
  const PAD = { top: 12, right: 12, bottom: 30, left: AXIS_W };
  const width = Math.max(points.length * 56, measured);
  const plotW = width - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;

  const top = niceCeil(max);
  const band = plotW / Math.max(1, points.length);
  const barW = Math.min(band * 0.3, 26);
  const y = (v: number) => PAD.top + plotH - (v / top) * plotH;

  if (points.length === 0 || max === 0) {
    return <div ref={box}><Empty>No cash movement recorded.</Empty></div>;
  }

  return (
    <div className="viz-cash" ref={box}>
      <div className="mb-3 flex flex-wrap items-center gap-4">
        <Key colour="var(--in)">Received</Key>
        <Key colour="var(--out)">Paid out</Key>
      </div>

      <div className="overflow-x-auto">
        <svg width={width} height={H} role="img" aria-label="Cash received and paid out by month">
          {[0, 0.5, 1].map((t) => (
            <g key={t}>
              <line className="viz-grid" x1={PAD.left} x2={width - PAD.right} y1={y(top * t)} y2={y(top * t)} />
              <text className="viz-axis" x={PAD.left - 8} y={y(top * t) + 3} textAnchor="end">{compact(top * t)}</text>
            </g>
          ))}

          {points.map((p, i) => {
            const cx = PAD.left + band * i + band / 2;

            return (
              <g key={p.month}>
                <rect
                  x={PAD.left + band * i} y={PAD.top} width={band} height={plotH}
                  fill="transparent"
                  onMouseEnter={() => setHovered(i)}
                  onMouseLeave={() => setHovered(null)}
                />
                {hovered === i && <rect className="viz-hot" x={PAD.left + band * i} y={PAD.top} width={band} height={plotH} />}

                <rect className="viz-bar" style={{ animationDelay: `${i * 50}ms` }}
                  x={cx - barW - 1} y={y(p.in)} width={barW} height={Math.max(0, plotH - (y(p.in) - PAD.top))} rx={3} fill="var(--in)" />
                <rect className="viz-bar" style={{ animationDelay: `${i * 50 + 25}ms` }}
                  x={cx + 1} y={y(p.out)} width={barW} height={Math.max(0, plotH - (y(p.out) - PAD.top))} rx={3} fill="var(--out)" />

                <text className="viz-axis" x={cx} y={H - 10} textAnchor="middle">{monthLabel(p.month)}</text>
              </g>
            );
          })}
        </svg>
      </div>

      {hovered !== null && points[hovered] && (
        <p className="mt-2 text-sm">
          <span className="font-medium text-text">{monthFull(points[hovered].month)}</span>
          <span className="text-muted"> · in </span>
          <span className="tabular text-text">{formatMoney(points[hovered].in)}</span>
          <span className="text-muted"> · out </span>
          <span className="tabular text-text">{formatMoney(points[hovered].out)}</span>
          <span className={points[hovered].in >= points[hovered].out ? "text-muted" : "text-warning-text"}>
            {" · net "}{formatMoney(points[hovered].in - points[hovered].out)}
          </span>
        </p>
      )}
    </div>
  );
}

// --- Ranked lists -------------------------------------------------------------------------------

/**
 * A ranked horizontal bar list — customers, or items.
 *
 * One series, so no legend: the panel title names what the bars are. The share is printed beside each
 * bar rather than encoded again in colour, because rank is already carried by position and length.
 */
export function RankedBars({
  rows,
  emptyLabel,
}: {
  rows: { label: string; value: number; share: number; note?: string }[];
  emptyLabel: string;
}) {
  if (rows.length === 0) return <Empty>{emptyLabel}</Empty>;

  const max = Math.max(...rows.map((r) => r.value));

  return (
    <div className="viz-ranked space-y-2.5">
      {rows.map((r, i) => (
        <div key={r.label} className="space-y-1">
          <div className="flex items-baseline justify-between gap-3">
            <span className="truncate text-sm text-text" title={r.label}>{r.label}</span>
            <span className="tabular shrink-0 text-sm text-text">
              {formatMoney(r.value)}
              <span className="ml-2 text-xs text-muted">{r.share.toFixed(1)}%</span>
            </span>
          </div>

          <div className="h-2 overflow-hidden rounded-sm bg-surface-sunken">
            <div
              className="viz-fill h-full rounded-sm"
              style={{
                width: `${Math.max(1, (r.value / max) * 100)}%`,
                background: "var(--rank)",
                animationDelay: `${i * 70}ms`,
              }}
            />
          </div>

          {r.note && <p className="text-xs text-muted">{r.note}</p>}
        </div>
      ))}
    </div>
  );
}

/** The change against the previous period — the thing that turns a total into a reading. */
export function Delta({ change, invert = false }: { change: number | null | undefined; invert?: boolean }) {
  if (change === null || change === undefined) {
    return <span className="text-muted">no prior month</span>;
  }

  const flat = Math.abs(change) < 0.05;
  // `invert` for figures where up is bad — overdue, money out. Direction and wording carry the meaning;
  // colour only agrees with them.
  const good = flat ? null : invert ? change < 0 : change > 0;

  return (
    <span className={good === null ? "text-muted" : good ? "text-success-text" : "text-warning-text"}>
      {flat ? "level" : `${change > 0 ? "▲" : "▼"} ${Math.abs(change).toFixed(1)}%`}
      <span className="text-muted"> vs last month</span>
    </span>
  );
}

/**
 * The chart palette, scoped per chart so each carries its own hues.
 *
 * Dark mode is a *selected* set of steps, not an automatic lightening: each was re-validated against the
 * dark surface, where the in-band lightness ceiling is lower than the light one — `#10b981` passes on
 * white and fails on the dark ground, which is why the emerald differs between the two.
 *
 * Both dark signals are covered: the OS preference *and* the explicit theme toggle, so a user who forces
 * light on a dark system still gets the light steps.
 */
export const ANALYTICS_VIZ_CSS = `
.viz-trend { --revenue: #4f46e5; --profit: #059669; }
.viz-cash { --in: #4f46e5; --out: #d97706; }
.viz-ranked { --rank: #4f46e5; }
.viz-ageing { --age-0: #d97706; --age-1: #b45309; --age-2: #92400e; --age-3: #7c2d12; }
.viz-trend .viz-grid, .viz-cash .viz-grid { stroke: #e1e0d9; stroke-width: 1; }
.viz-trend .viz-axis, .viz-cash .viz-axis { fill: #898781; font-size: 10px; }
.viz-trend .viz-hot, .viz-cash .viz-hot { fill: #1a1a19; opacity: 0.04; }
@media (prefers-color-scheme: dark) {
  :root:where(:not([data-theme="light"])) .viz-trend { --revenue: #6366f1; --profit: #0d9f70; }
  :root:where(:not([data-theme="light"])) .viz-cash { --in: #6366f1; --out: #d97706; }
  :root:where(:not([data-theme="light"])) .viz-ranked { --rank: #6366f1; }
  :root:where(:not([data-theme="light"])) .viz-ageing { --age-0: #f59e0b; --age-1: #d97706; --age-2: #b45309; --age-3: #92400e; }
  :root:where(:not([data-theme="light"])) .viz-trend .viz-grid,
  :root:where(:not([data-theme="light"])) .viz-cash .viz-grid { stroke: #2c2c2a; }
  :root:where(:not([data-theme="light"])) .viz-trend .viz-hot,
  :root:where(:not([data-theme="light"])) .viz-cash .viz-hot { fill: #ffffff; opacity: 0.05; }
}
:root[data-theme="dark"] .viz-trend { --revenue: #6366f1; --profit: #0d9f70; }
:root[data-theme="dark"] .viz-cash { --in: #6366f1; --out: #d97706; }
:root[data-theme="dark"] .viz-ranked { --rank: #6366f1; }
:root[data-theme="dark"] .viz-ageing { --age-0: #f59e0b; --age-1: #d97706; --age-2: #b45309; --age-3: #92400e; }
:root[data-theme="dark"] .viz-trend .viz-grid,
:root[data-theme="dark"] .viz-cash .viz-grid { stroke: #2c2c2a; }
:root[data-theme="dark"] .viz-trend .viz-hot,
:root[data-theme="dark"] .viz-cash .viz-hot { fill: #ffffff; opacity: 0.05; }

/* Bars grow out of the baseline, staggered left to right, so the eye reads the series in time order
   rather than being handed a finished block. transform-box and transform-origin are both needed on
   SVG: without them the scale happens about the viewport origin, not the bar own foot. */
.viz-bar {
  transform-box: fill-box;
  transform-origin: bottom;
  animation: viz-grow 520ms cubic-bezier(0.22, 1, 0.36, 1) backwards;
}

@keyframes viz-grow {
  from { transform: scaleY(0); opacity: 0.4; }
  to { transform: scaleY(1); opacity: 1; }
}

/* Horizontal bars (ageing, ranked lists) sweep out from the left on the same curve. */
.viz-fill {
  transform-origin: left center;
  animation: viz-sweep 560ms cubic-bezier(0.22, 1, 0.36, 1) backwards;
}

@keyframes viz-sweep {
  from { transform: scaleX(0); }
  to { transform: scaleX(1); }
}

/* Anybody who has asked for less motion gets the finished chart, immediately and still. */
@media (prefers-reduced-motion: reduce) {
  .viz-bar, .viz-fill { animation: none; }
}
`;
