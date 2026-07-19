import { useQuery } from "@tanstack/react-query";
import { me } from "./auth";

/**
 * Whether this user may see what things cost — and therefore what the business earns.
 *
 * **The server is the boundary; this only tidies the page.** Cost and profit are already withheld from
 * the response for a caller without margin access, so a screen that ignored this would show zeros and
 * nulls rather than leak anything. What this does is stop those empty columns being drawn at all, since
 * a Profit column full of noughts reads as "the business made nothing", which is worse than not asking
 * the question.
 *
 * Mirrors `MarginAccess.CanSee` on the server deliberately — same rule, stated in both places, because
 * the alternative is the page guessing from a shape in the payload.
 */
export function useMarginAccess(): boolean {
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const permissions = user.data?.permissions ?? [];

  return permissions.includes("dashboard") || permissions.includes("system.dev_admin");
}
