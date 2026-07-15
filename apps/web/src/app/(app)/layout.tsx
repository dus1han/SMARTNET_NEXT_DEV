import type { ReactNode } from "react";
import { AppShell } from "@/components/shell/app-shell";

/**
 * Everything a signed-in user sees sits inside the shell.
 *
 * Login and change-password deliberately do not: they are outside this route group, because a user
 * who cannot yet be identified has nothing to navigate to, and a sidebar full of links they cannot
 * open is not a welcome.
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  return <AppShell>{children}</AppShell>;
}
