# UI conventions

How every screen is built, so the current ones match and the next one matches without anyone having
to decide again. The rule of thumb: **if two screens need it, it lives in the design system, and a
screen reaches for the token or the component — never a raw value.**

The design system's single source of truth is [`apps/web/src/app/globals.css`](../apps/web/src/app/globals.css)
(tokens, both themes) and [`apps/web/src/components/ui`](../apps/web/src/components/ui) (primitives).

---

## 1. Colour — tokens, never raw palette

Screens use **semantic tokens** (`bg-surface`, `text-muted`, `border-subtle`, `text-primary`), never
`bg-neutral-900` or a hex. A token has a light and a dark value, so the whole app re-themes from one
file. Red / amber / green **mean** something (overdue, locked, paid) — never use them as decoration.

Both themes are always designed. Dark values live under `.dark` in `globals.css`; nothing is styled
"for dark" in a component.

## 2. Page structure — the same skeleton every time

```tsx
export default function ThingPage() {
  return (
    <FadeIn className="space-y-6">
      <PageHeader title="Things" description="…" actions={<Button>…</Button>} />
      {/* content */}
    </FadeIn>
  );
}
```

- **`FadeIn`** wraps the page content — one gentle entrance, every screen.
- **`PageHeader`** is the heading block (title + description + actions). One component, so they all
  line up. Never hand-roll an `<h1>`.
- The shell (`AppShell`) supplies the frame, the scroll container, and the max width. A screen renders
  only its content.

## 3. Lists — one `DataTable`, always

Every list is `DataTable` ([`components/data-table`](../apps/web/src/components/data-table)). A new
list is a **column set and a query**, nothing else — sorting, filtering, pagination, row hover, the
empty state, the loading skeleton, and the Excel export are all built in.

```tsx
<DataTable
  columns={columns}
  rows={query.data}
  loading={query.isPending}
  searchable={(r) => `${r.name} ${r.code}`}
  exportUrl="/api/things/export"        // a real server .xlsx — money as numeric cells
  defaultSort={{ id: "name" }}
  empty={{ title: "No things yet", description: "Add one to get started." }}
/>
```

Export is **server-side** (`IExcelExporter`) so a money column sums; never rebuild a spreadsheet in the
browser. Money is displayed with `toLocaleString(…, { minimumFractionDigits: 2 })` (see the report
pages), never routed through `lib/money.ts` (that is fils-based editor arithmetic, not display).

## 4. Headline figures — `StatTile`, pastel, counted up

KPIs and report summaries are `StatTile` ([`components/reports`](../apps/web/src/components/reports/report-shell.tsx)):
a soft **pastel** card (the `stat-*` classes in `globals.css`), the value in a deeper shade of its own
hue, every tile the **same height** (`h-full` in a stretch grid).

```tsx
<div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
  <StatTile label="Sales" icon={TrendingUp} color="indigo"  delayMs={0}
            value={<AnimatedNumber value={total} format={formatMoney} />} />
  <StatTile label="Profit" icon={Coins}     color="emerald" delayMs={70}
            value={<AnimatedNumber value={profit} format={formatMoney} />} />
  {/* … */}
</div>
```

- **Colours** — `indigo, emerald, amber, violet, sky, rose, slate`. Coordinated pastels; pick a small
  set per screen and keep them stable (colour follows the figure, not its rank).
- **`AnimatedNumber`** counts the value up on arrival (reduced-motion aware). Use it for every numeric
  headline.
- **`delayMs`** staggers a row (0, 70, 140, 210) so the group arrives as choreography.

## 5. Charts — validate the palette, keep to the family

Charts are hand-rolled SVG (no charting dependency). Before choosing colours, read the `dataviz` skill
and **run its validator** — never eyeball a palette. The dashboard's cash/credit series are emerald +
indigo, the same family as the pastel tiles, validated for colour-blind separation and contrast on
both surfaces. A legend is always present for ≥2 series; add a hover tooltip.

## 6. Motion — short, purposeful, and off when asked

House rule: **150–250ms, ease-out, nothing animates while the user is typing.** `prefers-reduced-motion`
is honoured globally in `globals.css` (and in JS for `AnimatedNumber`) — never negotiate it per screen.

- `FadeIn` — page entrance. `Stagger` — a grid that should arrive in sequence.
- Buttons press (`active:scale`), cards with `interactive` lift on hover, table rows lift on hover.
- The sign-in screen is the one exception allowed to be *inviting* (its slow aurora); everywhere past
  it, motion is functional.

## 7. Primitives — reach for these, don't rebuild

| Need | Use |
|---|---|
| Any button | `Button` (`primary` / `secondary` / `ghost` / `danger`; `pending` shows a spinner and disables) |
| A panel | `Card` (add `interactive` if it is clickable) |
| A field | `Input` / `Select` / `Textarea` / `Checkbox` — with a `label`, `error`, `hint` |
| A tag / status | `Badge` (`neutral` / `success` / `warning` / `danger`) |
| A modal | `Dialog` |
| An error surface | `ErrorBanner` (shows the correlation id) |
| Loading | `Skeleton` (shaped like the content) or `LoadingPanel` |
| A counted number | `AnimatedNumber` |

Scrollbars are themed globally (thin, rounded, floating) — nothing to do per screen.

## 8. Copy

Name things by what people recognise, not how the system is built. Active voice; a control says what it
does. Errors say what went wrong and how to fix it, and carry the correlation id.

---

**Adding a screen?** Start from an existing one in `app/(app)/` (the master-data pages are the
reference), keep the skeleton in §2, and you inherit all of the above for free.
