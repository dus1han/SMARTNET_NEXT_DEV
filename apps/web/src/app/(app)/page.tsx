const API = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

type Smoke = {
  companies: number;
  customers: number;
  items: number;
  invoices: number;
  invoiceLines: number;
  payments: number;
};

async function getSmoke(): Promise<Smoke | null> {
  try {
    const res = await fetch(`${API}/_smoke`, { cache: "no-store" });
    if (!res.ok) return null;
    return (await res.json()) as Smoke;
  } catch {
    return null;
  }
}

export default async function Home() {
  const smoke = await getSmoke();

  const rows: Array<[string, number]> = smoke
    ? [
        ["Companies", smoke.companies],
        ["Customers", smoke.customers],
        ["Items", smoke.items],
        ["Invoices", smoke.invoices],
        ["Invoice lines", smoke.invoiceLines],
        ["Payments", smoke.payments],
      ]
    : [];

  return (
    <main className="mx-auto flex min-h-screen max-w-2xl flex-col justify-center gap-8 p-8">
      <header>
        <h1 className="text-3xl font-semibold tracking-tight">Smartnet</h1>
        <p className="mt-1 text-sm text-neutral-500">
          Next.js + ASP.NET Core + MariaDB &middot; Phase 0
        </p>
      </header>

      {smoke ? (
        <section className="rounded-lg border border-neutral-200 dark:border-neutral-800">
          <div className="border-b border-neutral-200 px-4 py-3 dark:border-neutral-800">
            <span className="inline-flex items-center gap-2 text-sm font-medium">
              <span className="size-2 rounded-full bg-emerald-500" />
              API connected to <code className="text-xs">smartnet_invsys_dev</code>
            </span>
          </div>
          <dl className="divide-y divide-neutral-200 dark:divide-neutral-800">
            {rows.map(([label, value]) => (
              <div key={label} className="flex justify-between px-4 py-2.5 text-sm">
                <dt className="text-neutral-500">{label}</dt>
                <dd className="font-mono tabular-nums">{value.toLocaleString()}</dd>
              </div>
            ))}
          </dl>
        </section>
      ) : (
        <section className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm dark:border-amber-900 dark:bg-amber-950">
          <p className="font-medium">API unreachable</p>
          <p className="mt-1 text-neutral-600 dark:text-neutral-400">
            Start it with <code>dotnet run</code> in <code>apps/api/Smartnet.Api</code>, then
            reload.
          </p>
        </section>
      )}
    </main>
  );
}
