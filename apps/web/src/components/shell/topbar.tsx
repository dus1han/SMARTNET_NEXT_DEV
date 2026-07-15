"use client";

import * as Menu from "@radix-ui/react-dropdown-menu";
import { ChevronDown, LogOut, Menu as MenuIcon, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { useRouter } from "next/navigation";
import { logout, type Me } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { Button } from "@/components/ui";

const menuPanel = cn(
  "z-50 min-w-52 rounded-lg border border-subtle bg-surface p-1 shadow-lg",
  "data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95",
  "data-[state=closed]:animate-out data-[state=closed]:fade-out-0",
);

const menuItem = cn(
  "flex cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-sm text-text outline-none",
  "transition-colors duration-150 data-[highlighted]:bg-surface-sunken",
);

export function Topbar({ user, onOpenNav }: { user: Me; onOpenNav: () => void }) {
  return (
    <header className="flex h-16 shrink-0 items-center justify-between gap-3 border-b border-subtle bg-surface/80 px-4 backdrop-blur-sm sm:px-6">
      {/* No "working in" switcher. Smart Net and Smart Technologies are trading entities, not
          tenants (see ICompanyAccessService) — every user sees both, and which entity a document
          belongs to is chosen on the document itself. A global company switch here only invited the
          question "chosen for what?", and the answer was "nothing you can see". The one place the
          entity is genuinely a choice — configuring its header, tax and numbering — makes that choice
          on the Settings page. */}
      <Button
        variant="ghost"
        size="icon"
        onClick={onOpenNav}
        className="lg:hidden"
        aria-label="Open navigation"
      >
        <MenuIcon />
      </Button>

      {/* Pushes the controls to the right on desktop, where the button above is hidden. */}
      <div className="hidden lg:block" />

      <div className="flex items-center gap-1">
        <ThemeToggle />
        <UserMenu user={user} />
      </div>
    </header>
  );
}

function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme();
  const dark = resolvedTheme === "dark";

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={() => setTheme(dark ? "light" : "dark")}
      aria-label={dark ? "Switch to light theme" : "Switch to dark theme"}
    >
      {/* Both are rendered and cross-faded, so the icon does not pop in after hydration. */}
      <Sun className={cn("absolute transition-all duration-200", dark ? "scale-100 rotate-0" : "scale-0 -rotate-90")} />
      <Moon className={cn("transition-all duration-200", dark ? "scale-0 rotate-90" : "scale-100 rotate-0")} />
    </Button>
  );
}

function UserMenu({ user }: { user: Me }) {
  const router = useRouter();

  const initials = user.username.slice(0, 2).toUpperCase();

  return (
    <Menu.Root>
      <Menu.Trigger
        className={cn(
          "flex items-center gap-2 rounded-lg px-1.5 py-1.5",
          "transition-colors duration-200 hover:bg-surface-sunken",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/30",
        )}
      >
        <span className="grid size-8 place-items-center rounded-full bg-primary text-xs font-semibold text-primary-text">
          {initials}
        </span>
        <ChevronDown className="size-3.5 text-muted" aria-hidden />
      </Menu.Trigger>

      <Menu.Portal>
        <Menu.Content align="end" sideOffset={6} className={menuPanel}>
          <div className="px-2.5 py-2">
            <p className="truncate text-sm font-medium text-text">{user.username}</p>
            <p className="text-xs text-muted">
              {user.permissions.length} permission{user.permissions.length === 1 ? "" : "s"}
            </p>
          </div>

          <Menu.Separator className="my-1 h-px bg-subtle" />

          <Menu.Item
            className={menuItem}
            onSelect={async () => {
              await logout();
              router.push("/login");
              router.refresh();
            }}
          >
            <LogOut className="size-4 text-muted" aria-hidden />
            Sign out
          </Menu.Item>
        </Menu.Content>
      </Menu.Portal>
    </Menu.Root>
  );
}
