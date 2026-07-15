import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * Merges Tailwind classes, with later ones winning.
 *
 * `clsx` alone would produce `px-2 px-4` and leave the browser to pick — which it does by source
 * order in the stylesheet, not by the order you wrote them, so a caller cannot reliably override a
 * component's padding. `twMerge` resolves the conflict properly, which is what makes a `className`
 * prop on a component actually mean something.
 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
