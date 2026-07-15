"use client";

import { useIsFetching, useIsMutating } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { cn } from "@/lib/cn";

/**
 * The thin bar across the top of the window while anything is in flight.
 *
 * It is driven by TanStack Query's in-flight counts rather than by route events, which means it
 * covers what the user is actually waiting for: the data. A route that renders instantly and then
 * shows an empty table for 400ms has not finished loading, whatever the router thinks.
 *
 * <p>It fades in only after a short delay. A bar that flashes on every 80ms request is worse than no
 * bar — it reads as flicker, and the eye learns to ignore it, which defeats the point of having one
 * when a request really is slow.</p>
 */
export function RouteProgress() {
  const fetching = useIsFetching();
  const mutating = useIsMutating();

  const busy = fetching + mutating > 0;
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    // Both transitions go through a timer, never a synchronous setState in the effect body — React
    // treats that as a cascading render, and it is: it schedules a second pass before the first has
    // painted.
    //
    // The 180ms delay on the way IN is the point of the component. A bar that flashes on every 80ms
    // request reads as flicker, and the eye learns to ignore it — which is exactly when you need it
    // to be believed. Hiding is immediate (0ms), because a bar that lingers after the data has
    // landed is a lie about what the app is doing.
    const timer = setTimeout(() => setVisible(busy), busy ? 180 : 0);

    return () => clearTimeout(timer);
  }, [busy]);

  return (
    <div
      aria-hidden
      className={cn(
        "pointer-events-none fixed inset-x-0 top-0 z-100 h-0.5",
        "transition-opacity duration-200",
        visible ? "opacity-100" : "opacity-0",
      )}
    >
      <div className="h-full w-1/3 animate-indeterminate bg-linear-to-r from-transparent via-primary to-transparent" />
    </div>
  );
}
