import {
  Banknote,
  BarChart3,
  Building2,
  FileMinus,
  FileStack,
  FileText,
  FlaskConical,
  HandCoins,
  LayoutDashboard,
  Package,
  Percent,
  Receipt,
  Scale,
  ScrollText,
  Settings,
  ShieldCheck,
  ShoppingBag,
  ShoppingCart,
  ShieldAlert,
  Trash2,
  TrendingUp,
  Truck,
  Users,
  Wallet,
  Wrench,
  type LucideIcon,
} from "lucide-react";

export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;

  /**
   * The permission required to *see* this link.
   *
   * Seeing is not doing. The endpoint behind every one of these denies by default and re-checks the
   * same permission on the server, so hiding a link is a courtesy to the user, not a control on
   * them. Getting that backwards is ISSUES A5 — the legacy app hid the menu item and left the
   * endpoint wide open, which meant any logged-in user could make themselves an administrator.
   */
  permission?: string;

  /** Not built yet. Shown greyed out, so the shape of the finished app is visible from day one. */
  phase?: string;
}

export interface NavSection {
  title: string;
  items: NavItem[];
}

export const NAVIGATION: NavSection[] = [
  {
    title: "Overview",
    items: [
      { href: "/", label: "Dashboard", icon: LayoutDashboard },
    ],
  },
  {
    title: "Sales",
    items: [
      { href: "/quotations", label: "Quotations", icon: FileText, permission: "search_qu" },
      { href: "/invoices", label: "Invoices", icon: Receipt, permission: "search_in" },
      { href: "/invoices/deleted", label: "Deleted invoices", icon: Trash2, permission: "deleted_in" },
      { href: "/credit-notes", label: "Credit notes", icon: FileMinus, permission: "search_cn" },
      { href: "/payments", label: "Payments", icon: Wallet, permission: "payments" },
    ],
  },
  {
    title: "Purchasing",
    items: [
      { href: "/purchase-orders", label: "Purchase orders", icon: ShoppingCart, permission: "purchaseorder" },
      { href: "/supplier-invoices", label: "Supplier invoices", icon: FileStack, permission: "supplier_in" },
      { href: "/supplier-payments", label: "Supplier payments", icon: HandCoins, permission: "supplier_in" },
      { href: "/cheques", label: "Cheques", icon: ScrollText, permission: "cheques" },
      { href: "/expenses", label: "Expenses", icon: Banknote, permission: "expenses" },
    ],
  },
  {
    title: "Service",
    items: [
      { href: "/job-cards", label: "Job cards", icon: Wrench, permission: "jobcards" },
    ],
  },
  {
    title: "Master data",
    items: [
      { href: "/customers", label: "Customers", icon: Building2, permission: "customer_m" },
      { href: "/suppliers", label: "Suppliers", icon: Truck, permission: "supplier_m" },
      { href: "/items", label: "Items", icon: Package, permission: "item_m" },
    ],
  },
  {
    title: "Reports",
    items: [
      { href: "/reports/trial-balance", label: "Trial balance", icon: Scale, permission: "general_ledger" },
      { href: "/reports/profit-loss", label: "Profit & loss", icon: TrendingUp, permission: "general_ledger" },
      { href: "/reports/sales", label: "Sales", icon: BarChart3, permission: "sales_rpt" },
      { href: "/reports/customer-sales", label: "Customer sales", icon: Users, permission: "customersales_rpt" },
      { href: "/reports/expenses", label: "Expenses", icon: Banknote, permission: "expenses_rpt" },
      { href: "/reports/cheques", label: "Cheques", icon: ScrollText, permission: "chequerpt" },
      { href: "/reports/job-cards", label: "Job cards", icon: Wrench, permission: "jobcards_rpt" },
      { href: "/reports/customer-vat", label: "Customer VAT", icon: Percent, permission: "cusvat_rpt" },
      { href: "/reports/supplier-vat", label: "Supplier VAT", icon: Percent, permission: "suppliervat_rpt" },
      { href: "/reports/supplier-purchase", label: "Supplier purchases", icon: ShoppingBag, permission: "supplierpurchase_rpt" },
      { href: "/reports/supplier-payments", label: "Supplier payments", icon: HandCoins, permission: "supplierpayments_rpt" },
      { href: "/reports/outstanding", label: "Outstanding", icon: Wallet, permission: "customer_outstanding" },
      { href: "/reports/data-exceptions", label: "Data exceptions", icon: ShieldAlert, permission: "general_ledger" },
    ],
  },
  {
    title: "Administration",
    items: [
      { href: "/users", label: "Users", icon: Users, permission: "users" },
      { href: "/audit", label: "Audit log", icon: ShieldCheck, permission: "audit.view" },
      { href: "/settings", label: "Settings", icon: Settings, permission: "settings.manage" },
    ],
  },
  {
    // Not a feature, and deliberately visible to everyone: the whole purpose of the line-item
    // editor prototype is that the people who type invoices all day can reach it and tell us it is
    // wrong before Phase 5 is built on top of it. It carries no permission because it touches no
    // data — there is no endpoint behind it at all.
    title: "Prototypes",
    items: [
      { href: "/prototypes/line-items", label: "Line-item editor", icon: FlaskConical },
    ],
  },
];

/** Hides a section entirely when the user may see nothing in it. An empty heading is clutter. */
export function visibleSections(permissions: string[]): NavSection[] {
  return NAVIGATION.map((section) => ({
    ...section,
    items: section.items.filter(
      (item) => !item.permission || permissions.includes(item.permission),
    ),
  })).filter((section) => section.items.length > 0);
}
