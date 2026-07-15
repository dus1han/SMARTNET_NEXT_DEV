import { cn } from "@/lib/cn";

/**
 * The system's brand, in one place.
 *
 * The name and the mark appear on the sidebar, the login panel, the phone header and the browser tab.
 * They lived as a copy-pasted letter "S" on each of those until they were centralised here — the same
 * reason the login logo was already a single `Mark()`: three surfaces showing three slightly different
 * logos is how a rebrand gets left half-done.
 */
export const BRAND_NAME = "INVOSYS";
export const BRAND_TAGLINE = "Inventory & invoicing";

/**
 * The INVOSYS glyph — a stacked inventory cube, drawn in `currentColor` so it takes the colour of
 * whatever it sits on (white on the brand square, and it would invert cleanly anywhere else).
 */
export function BrandGlyph({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true" className={className}>
      {/* The lid, then the two side faces at falling opacity — enough to read as a box at 16px
          without any of the detail that turns to mud at that size. */}
      <path d="M12 2.25 20.75 7 12 11.75 3.25 7 12 2.25Z" fill="currentColor" />
      <path d="M3.25 7 12 11.75V21.5L3.25 16.75V7Z" fill="currentColor" fillOpacity="0.72" />
      <path d="M20.75 7 12 11.75V21.5L20.75 16.75V7Z" fill="currentColor" fillOpacity="0.46" />
    </svg>
  );
}

/**
 * The logo mark: the glyph on the brand-coloured square. Callers set the square's size and rounding
 * (and any shadow) through `className`; the glyph scales to it.
 */
export function BrandMark({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        "grid shrink-0 place-items-center rounded-xl bg-primary text-primary-text shadow-sm shadow-primary/30",
        className,
      )}
    >
      <BrandGlyph className="h-3/5 w-3/5" />
    </div>
  );
}
