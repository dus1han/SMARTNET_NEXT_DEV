"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";

/**
 * A number that counts up to its value when it first appears (and animates between values after).
 *
 * The point is small: a KPI that lands by counting reads as "this was just calculated for you",
 * which is most of what makes a dashboard feel alive rather than static. It is deliberately quick
 * (≈700ms) and eases out, in keeping with the house motion rules.
 *
 * `prefers-reduced-motion` is honoured directly here, not just by the global CSS rule: this animation
 * is JavaScript driving React state, which that rule cannot reach. For someone who asked for less
 * motion, the number simply appears at its final value.
 */
export function AnimatedNumber({ value, format, durationMs = 700, className }: {
  value: number;
  format: (n: number) => string;
  durationMs?: number;
  className?: string;
}) {
  const [display, setDisplay] = useState(0);
  const fromRef = useRef(0);
  const frame = useRef<number | null>(null);

  useEffect(() => {
    const from = fromRef.current;
    const to = Number.isFinite(value) ? value : 0;

    const reduce =
      typeof window !== "undefined" &&
      window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

    if (reduce || from === to) {
      setDisplay(to);
      fromRef.current = to;
      return;
    }

    let start: number | null = null;

    const tick = (now: number) => {
      start ??= now;
      const t = Math.min(1, (now - start) / durationMs);
      const eased = 1 - (1 - t) ** 3; // ease-out cubic
      setDisplay(from + (to - from) * eased);

      if (t < 1) {
        frame.current = requestAnimationFrame(tick);
      } else {
        fromRef.current = to;
      }
    };

    frame.current = requestAnimationFrame(tick);

    return () => {
      if (frame.current !== null) cancelAnimationFrame(frame.current);
    };
  }, [value, durationMs]);

  return <span className={cn("tabular", className)}>{format(display)}</span>;
}
