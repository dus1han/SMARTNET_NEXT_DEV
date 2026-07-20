"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, Hash, Mail, Percent, SlidersHorizontal, type LucideIcon } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api";
import {
  RULE_LABELS,
  createCompany,
  deleteCompanyLogo,
  getBusinessRules,
  getCompany,
  getCompanyLogoUrl,
  getMailSettings,
  getTaxRates,
  listCompanies,
  saveBusinessRules,
  saveCompany,
  setVatRate,
  updateTaxRateFrom,
  uploadCompanyLogo,
  saveMailSettings,
  sendTestEmail,
  type BusinessRule,
  type CompanyProfile,
  type CompanySummary,
  type MailSettings,
  type CompanyCreated,
  type TaxRate,
} from "@/lib/settings";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { me } from "@/lib/auth";
import { cn } from "@/lib/cn";
import { Numbering } from "@/components/numbering";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

/**
 * Settings — every constant the old system buried in its source code, editable here.
 *
 * Two decisions shape this screen:
 *
 *  1. **It is organised by section, not stacked.** The legacy equivalent was one long scroll; here
 *     the sections are a nav on the left and one panel on the right, so an administrator changing the
 *     VAT rate is not scrolling past the SMTP password to find it.
 *
 *  2. **Everything on it belongs to one company at a time.** Smart Net and Smart Technologies each
 *     have their own letterhead, tax rates, numbering and mail — and there is no longer a global
 *     "working in" switcher to imply otherwise. So the entity is chosen *here*, at the top, and every
 *     section reflects the one that is selected. This is the one place the company is a real choice.
 */

type SectionKey = "company" | "rules" | "tax" | "numbering" | "mail";

const SECTIONS: { key: SectionKey; label: string; icon: LucideIcon; blurb: string }[] = [
  { key: "company", label: "Company details", icon: Building2, blurb: "Printed on every document." },
  { key: "rules", label: "Business rules", icon: SlidersHorizontal, blurb: "Credit, discounts, terms." },
  { key: "tax", label: "Tax rates", icon: Percent, blurb: "Applied to new documents." },
  { key: "numbering", label: "Document numbering", icon: Hash, blurb: "Prefixes and counters." },
  { key: "mail", label: "Outgoing mail", icon: Mail, blurb: "SMTP, and a send test." },
];

export default function SettingsPage() {
  const queryClient = useQueryClient();
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const [companyId, setCompanyId] = useState<number | null>(null);
  const [section, setSection] = useState<SectionKey>("company");
  const [adding, setAdding] = useState(false);
  const [addingRate, setAddingRate] = useState(false);

  const active = companyId ?? companies.data?.[0]?.id ?? null;

  // Adding a company or a tax rate are Dev_Admin's, not an ordinary administrator's — both are
  // money/structure, not presentation, and both are enforced server-side. Hiding the buttons is a
  // courtesy, not the control: the lesson of ISSUES A5 is that the legacy app hid menu items while
  // leaving the endpoints behind them open to anyone signed in.
  const isDevAdmin = user.data?.permissions.includes("system.dev_admin") ?? false;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Settings"
        description="Every one of these is a constant in the old system's source code. Changing one used to mean a deployment."
      />

      {companies.error && (
        <ErrorBanner
          message={(companies.error as ApiError).message}
          correlationId={(companies.error as ApiError).correlationId}
        />
      )}

      {companies.isPending ? (
        <Skeleton className="h-64" />
      ) : active === null ? (
        <Card>
          <p className="text-sm text-muted">You do not have access to any company to configure.</p>
        </Card>
      ) : (
        <>
          <div className="flex flex-wrap items-end gap-4">
            <CompanyPicker
              companies={companies.data ?? []}
              active={active}
              onChange={setCompanyId}
            />

            {isDevAdmin && (
              <div className="ml-auto flex gap-2">
                <Button variant="secondary" onClick={() => setAddingRate(true)}>
                  Set VAT rate
                </Button>
                <Button variant="secondary" onClick={() => setAdding(true)}>
                  Add company
                </Button>
              </div>
            )}
          </div>

          {adding && (
            <AddCompanyDialog
              onCancel={() => setAdding(false)}
              onCreated={async (created) => {
                setAdding(false);
                await queryClient.invalidateQueries({ queryKey: ["companies"] });
                // Land on the new entity, on its details, because that is what anyone who just made
                // one wants next — the address and bank details it was not asked for.
                setCompanyId(created.id);
                setSection("company");
              }}
            />
          )}

          {addingRate && (
            <SetVatRateDialog
              onCancel={() => setAddingRate(false)}
              onSaved={async (companiesAffected) => {
                setAddingRate(false);
                toast.success(
                  companiesAffected === 1
                    ? "VAT rate set for the VAT-registered company."
                    : `VAT rate set across ${companiesAffected} VAT-registered companies.`,
                );
                await queryClient.invalidateQueries({ queryKey: ["tax-rates"] });
                // Show the result: jump to the tax section, wherever they were when they set it.
                setSection("tax");
              }}
            />
          )}

          <div className="grid gap-6 lg:grid-cols-[13rem_1fr]">
            <SectionNav section={section} onChange={setSection} />

            {/* Keyed by the company, so switching entity remounts the section with that entity's
                data rather than leaving the previous one's edits in the form. */}
            <div key={`${active}-${section}`} className="min-w-0">
              <Section which={section} companyId={active} />
            </div>
          </div>
        </>
      )}
    </FadeIn>
  );
}

/**
 * Add a trading entity.
 *
 * It asks for a name, a number prefix and whether it charges VAT, and provisions the rest — because the
 * alternative is a company that exists and cannot do anything. A bare `companies_m` row has no tax rate
 * (so the engine refuses to rate any document it raises), no numbering series, and no email templates,
 * and there is no screen anywhere that would let someone add the templates afterwards.
 *
 * <b>No VAT rate is asked for.</b> A VAT-registered company inherits the rate the others charge today;
 * VAT is not a per-company figure to re-type. The rest of the profile (address, bank, logo, VAT number)
 * is edited on this same screen a moment later.
 */
function AddCompanyDialog({ onCancel, onCreated }: {
  onCancel: () => void;
  onCreated: (created: CompanyCreated) => void | Promise<void>;
}) {
  const [name, setName] = useState("");
  const [vatRegistered, setVatRegistered] = useState(true);
  const [brc, setBrc] = useState("");
  const [prefix, setPrefix] = useState("");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const valid =
    name.trim() !== ""
    && prefix.trim() !== ""
    && reason.trim().length >= MINIMUM_REASON_LENGTH;

  async function submit() {
    setSaving(true);
    try {
      const created = await createCompany(
        {
          name: name.trim(),
          isVatRegistered: vatRegistered,
          businessRegistrationNo: brc.trim() === "" ? null : brc.trim(),
          numberPrefix: prefix.trim(),
        },
        reason.trim(),
      );

      toast.success(
        `${created.name} created — ${created.taxRatesCreated} tax rates, `
        + `${created.numberSeriesCreated} numbering series, ${created.emailTemplatesCreated} email templates.`,
      );

      await onCreated(created);
    } catch (error: unknown) {
      toast.error(message(error));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onCancel()}
      title="Add a company"
      description="A second trading entity, with its own document numbering and letterhead. Everyone who uses the system will be able to work in it."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={submit} pending={saving} disabled={!valid}>
            Create
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Smart Solar (Pvt) Ltd"
          hint="As it should appear on documents."
        />

        <Input
          label="Document number prefix"
          value={prefix}
          onChange={(e) => setPrefix(e.target.value)}
          placeholder="SS-"
          hint="Applied to all nine document types; each is editable afterwards under Numbering. {YY}{MON}_SS_ works too."
        />

        <Input
          label="Business registration no."
          value={brc}
          onChange={(e) => setBrc(e.target.value)}
        />

        <label className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            checked={vatRegistered}
            onChange={(e) => setVatRegistered(e.target.checked)}
            className="mt-1"
          />
          <span>
            <span className="text-text">Registered for VAT</span>
            <span className="block text-xs text-muted">
              Ticked, it inherits the VAT rate the other registered companies charge today. Unticked, it is
              taxed at 0% and carries a zero rate only. The VAT number is added afterwards, in Company
              details.
            </span>
          </span>
        </label>

        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
        />
      </div>
    </Dialog>
  );
}

/** Which entity is being configured. Hidden when there is only one — a choice of one is not a choice. */
function CompanyPicker({ companies, active, onChange }: {
  companies: CompanySummary[];
  active: number;
  onChange: (id: number) => void;
}) {
  if (companies.length <= 1) return null;

  return (
    <div>
      <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted">Configuring</p>

      <div className="inline-flex flex-wrap gap-1 rounded-xl border border-subtle bg-surface-sunken p-1">
        {companies.map((company) => {
          const selected = company.id === active;

          return (
            <button
              key={company.id}
              type="button"
              aria-pressed={selected}
              onClick={() => onChange(company.id)}
              className={cn(
                "flex items-center gap-2 rounded-lg px-3.5 py-2 text-sm font-medium",
                "transition-colors duration-200 ease-out",
                selected ? "bg-surface text-text shadow-sm" : "text-muted hover:text-text",
              )}
            >
              <span
                className={cn(
                  "grid size-5 place-items-center rounded text-[10px] font-bold",
                  selected ? "bg-primary text-primary-text" : "bg-primary-ghost text-primary",
                )}
              >
                {initials(company.name)}
              </span>
              {company.name}
              {company.isVatRegistered && (
                <span className="rounded bg-primary-ghost px-1.5 py-0.5 text-[10px] text-primary">
                  VAT
                </span>
              )}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/** The section rail: vertical on desktop, a horizontal scroller on a phone. */
function SectionNav({ section, onChange }: {
  section: SectionKey;
  onChange: (key: SectionKey) => void;
}) {
  return (
    <nav className="flex gap-1 overflow-x-auto lg:flex-col lg:overflow-visible">
      {SECTIONS.map((item) => {
        const selected = item.key === section;

        return (
          <button
            key={item.key}
            type="button"
            aria-current={selected ? "page" : undefined}
            onClick={() => onChange(item.key)}
            className={cn(
              "flex shrink-0 items-center gap-3 rounded-lg px-3 py-2.5 text-left",
              "transition-colors duration-150 ease-out",
              selected
                ? "bg-primary-ghost text-primary"
                : "text-muted hover:bg-surface-sunken hover:text-text",
            )}
          >
            <item.icon className="size-4 shrink-0" aria-hidden />
            <span className="min-w-0">
              <span className="block text-sm font-medium">{item.label}</span>
              <span className="hidden text-xs opacity-70 lg:block">{item.blurb}</span>
            </span>
          </button>
        );
      })}
    </nav>
  );
}

function Section({ which, companyId }: { which: SectionKey; companyId: number }) {
  switch (which) {
    case "company":
      return <CompanySection companyId={companyId} />;
    case "rules":
      return <RulesSection companyId={companyId} />;
    case "tax":
      return <TaxSection companyId={companyId} />;
    case "numbering":
      return (
        <Numbering
          companyId={companyId}
          onError={(error) => toast.error(message(error))}
          onDone={(what) => toast.success(what)}
        />
      );
    case "mail":
      return <MailSection companyId={companyId} />;
  }
}

// --- Company ---------------------------------------------------------------------------------

function CompanySection({ companyId }: { companyId: number }) {
  const queryClient = useQueryClient();
  const company = useQuery({ queryKey: ["company", companyId], queryFn: () => getCompany(companyId) });

  if (company.isPending) return <Skeleton className="h-96" />;
  if (company.error || !company.data) return <LoadError error={company.error} />;

  return (
    <CompanyForm
      profile={company.data}
      companyId={companyId}
      onSave={(profile, reason) =>
        saveCompany(profile, reason, companyId)
          .then(() => {
            toast.success("Company details saved.");
            void queryClient.invalidateQueries({ queryKey: ["company", companyId] });
          })
          .catch((error: unknown) => toast.error(message(error)))
      }
    />
  );
}

/** Upload / preview / remove a company's logo — its own action (a binary upload), not part of the form save. */
function CompanyLogoField({ companyId, hasLogo }: { companyId: number; hasLogo: boolean }) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let active = true;
    let objectUrl: string | null = null;
    void (async () => {
      const next = hasLogo ? await getCompanyLogoUrl(companyId) : null;
      if (!active) {
        if (next) URL.revokeObjectURL(next);
        return;
      }
      objectUrl = next;
      setUrl(next);
    })();
    return () => {
      active = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [companyId, hasLogo]);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["company", companyId] });

  const upload = async (file: File) => {
    setBusy(true);
    try {
      await uploadCompanyLogo(companyId, file);
      toast.success("Logo updated.");
      void refresh();
    } catch (error) {
      toast.error(message(error));
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  };

  const remove = async () => {
    setBusy(true);
    try {
      await deleteCompanyLogo(companyId);
      toast.success("Logo removed.");
      void refresh();
    } catch (error) {
      toast.error(message(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="flex items-center gap-4">
        <div className="flex h-16 w-28 items-center justify-center overflow-hidden rounded-md border border-subtle bg-surface">
          {url ? (
            // eslint-disable-next-line @next/next/no-img-element -- a blob object URL, not a static asset
            <img src={url} alt="Company logo" className="max-h-full max-w-full object-contain" />
          ) : (
            <span className="text-xs text-muted">No logo</span>
          )}
        </div>
        <div className="flex flex-col gap-2">
          <input
            ref={inputRef}
            type="file"
            accept="image/png,image/jpeg,image/gif,image/webp,image/svg+xml"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) void upload(file);
            }}
          />
          <Button variant="secondary" size="sm" pending={busy} onClick={() => inputRef.current?.click()}>
            {hasLogo ? "Replace logo" : "Upload logo"}
          </Button>
          {hasLogo && (
            <Button variant="ghost" size="sm" disabled={busy} onClick={remove}>
              Remove
            </Button>
          )}
        </div>
      </div>
      <p className="mt-2 text-xs text-muted">
        PNG, JPEG, GIF, WebP or SVG · up to 2 MB · prints on this company&rsquo;s documents beside the name.
      </p>
    </div>
  );
}

function CompanyForm({ profile, companyId, onSave }: {
  profile: CompanyProfile;
  companyId: number;
  onSave: (profile: CompanyProfile, reason: string) => void;
}) {
  const [draft, setDraft] = useState(profile);
  const [reason, setReason] = useState("");

  const set = <K extends keyof CompanyProfile>(key: K, value: CompanyProfile[K]) =>
    setDraft((d) => ({ ...d, [key]: value }));

  return (
    <Card>
      <SectionHeading
        title="Company details"
        blurb="Printed on every document — the header, the bank block, the brand colour. Hardcoded in the Crystal Reports templates today, which is why changing an address needs a developer."
      />

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <Input label="Name" value={draft.name} onChange={(e) => set("name", e.target.value)} />
        <Input
          label="VAT number"
          value={draft.vatNumber ?? ""}
          disabled={!draft.isVatRegistered}
          hint={!draft.isVatRegistered ? "Not registered for VAT." : undefined}
          onChange={(e) => set("vatNumber", e.target.value)}
        />
        <Input
          label="Business registration no"
          value={draft.businessRegistrationNo ?? ""}
          hint="Printed on document headers."
          onChange={(e) => set("businessRegistrationNo", e.target.value)}
        />
        <Input
          label="Address line 1"
          value={draft.addressLine1 ?? ""}
          onChange={(e) => set("addressLine1", e.target.value)}
        />
        <Input
          label="Address line 2"
          value={draft.addressLine2 ?? ""}
          onChange={(e) => set("addressLine2", e.target.value)}
        />
        <Input label="City" value={draft.city ?? ""} onChange={(e) => set("city", e.target.value)} />
        <Input
          label="Country"
          value={draft.country ?? ""}
          onChange={(e) => set("country", e.target.value)}
        />
        <Input label="Phone" value={draft.phone ?? ""} onChange={(e) => set("phone", e.target.value)} />
        <Input label="Email" type="email" value={draft.email ?? ""} onChange={(e) => set("email", e.target.value)} />
        <Input
          label="Website"
          value={draft.website ?? ""}
          onChange={(e) => set("website", e.target.value)}
        />
      </div>

      <div className="mt-5">
        <p className="mb-3 text-xs font-medium uppercase tracking-wide text-muted">Bank details</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Bank name"
            value={draft.bankName ?? ""}
            onChange={(e) => set("bankName", e.target.value)}
          />
          <Input
            label="Branch"
            value={draft.bankBranch ?? ""}
            onChange={(e) => set("bankBranch", e.target.value)}
          />
          <Input
            label="Account name"
            value={draft.bankAccountName ?? ""}
            onChange={(e) => set("bankAccountName", e.target.value)}
          />
          <Input
            label="Account number"
            value={draft.bankAccountNumber ?? ""}
            onChange={(e) => set("bankAccountNumber", e.target.value)}
          />
        </div>
      </div>

      <div className="mt-5">
        <p className="mb-3 text-xs font-medium uppercase tracking-wide text-muted">Branding</p>
        <CompanyLogoField companyId={companyId} hasLogo={profile.hasLogo} />
      </div>

      <div className="mt-5 flex flex-wrap items-end gap-6">
        <label className="flex items-center gap-3">
          <span className="text-sm text-text">Brand colour</span>
          <input
            type="color"
            value={draft.brandColour ?? "#0f172a"}
            onChange={(e) => set("brandColour", e.target.value)}
            className="size-9 cursor-pointer rounded-md border border-subtle bg-surface"
            aria-label="Brand colour"
          />
        </label>

        <div>
          <label className="flex items-center gap-2 text-sm text-text">
            <input
              type="checkbox"
              className="size-4 rounded border-subtle text-primary focus-visible:ring-2 focus-visible:ring-ring/25"
              checked={draft.isVatRegistered}
              onChange={(e) => set("isVatRegistered", e.target.checked)}
            />
            Registered for VAT
          </label>
          <p className="mt-1 text-xs text-muted">
            Unticking clears the VAT number — an unregistered company must not print one.
          </p>
        </div>
      </div>

      <ReasonAndSave
        reason={reason}
        setReason={setReason}
        onSave={() =>
          onSave({ ...draft, vatNumber: draft.isVatRegistered ? draft.vatNumber : null }, reason)
        }
        label="Save company details"
      />
    </Card>
  );
}

// --- Business rules --------------------------------------------------------------------------

function RulesSection({ companyId }: { companyId: number }) {
  const queryClient = useQueryClient();
  const rules = useQuery({
    queryKey: ["business-rules", companyId],
    queryFn: () => getBusinessRules(companyId),
  });

  if (rules.isPending) return <Skeleton className="h-80" />;
  if (rules.error || !rules.data) return <LoadError error={rules.error} />;

  return (
    <RulesForm
      rules={rules.data}
      onSave={(next, reason) =>
        saveBusinessRules(next, reason, companyId)
          .then(() => {
            toast.success("Business rules saved.");
            void queryClient.invalidateQueries({ queryKey: ["business-rules", companyId] });
          })
          .catch((error: unknown) => toast.error(message(error)))
      }
    />
  );
}

function RulesForm({ rules, onSave }: {
  rules: BusinessRule[];
  onSave: (rules: BusinessRule[], reason: string) => void;
}) {
  const [draft, setDraft] = useState(rules);
  const [reason, setReason] = useState("");

  const set = (key: string, value: string) =>
    setDraft((current) => current.map((r) => (r.key === key ? { ...r, value } : r)));

  return (
    <Card>
      <SectionHeading
        title="Business rules"
        blurb="The seven rules the old system compiled in. Credit limits, for instance, are enforced today only on service invoices — and only because that is what the code happens to do."
      />

      <div className="mt-5 divide-y divide-subtle">
        {draft.map((rule) => {
          const isBool = rule.value === "true" || rule.value === "false";

          return (
            <div key={rule.key} className="flex flex-wrap items-center justify-between gap-3 py-3">
              <label className="text-sm text-text" htmlFor={rule.key}>
                {RULE_LABELS[rule.key] ?? rule.key}
              </label>

              {isBool ? (
                <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-muted">
                  <input
                    id={rule.key}
                    type="checkbox"
                    className="size-4 rounded border-subtle text-primary focus-visible:ring-2 focus-visible:ring-ring/25"
                    checked={rule.value === "true"}
                    onChange={(e) => set(rule.key, String(e.target.checked))}
                  />
                  {rule.value === "true" ? "On" : "Off"}
                </label>
              ) : (
                <input
                  id={rule.key}
                  value={rule.value}
                  onChange={(e) => set(rule.key, e.target.value)}
                  className="w-32 rounded-md border border-subtle bg-surface px-2.5 py-1.5 text-right text-sm tabular text-text focus:border-primary focus:outline-none"
                />
              )}
            </div>
          );
        })}
      </div>

      <ReasonAndSave
        reason={reason}
        setReason={setReason}
        onSave={() => onSave(draft, reason)}
        label="Save business rules"
      />
    </Card>
  );
}

// --- Tax rates -------------------------------------------------------------------------------

function TaxSection({ companyId }: { companyId: number }) {
  const queryClient = useQueryClient();
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const [editing, setEditing] = useState<TaxRate | null>(null);

  const taxRates = useQuery({
    queryKey: ["tax-rates", companyId],
    queryFn: () => getTaxRates(companyId),
  });

  if (taxRates.isPending) return <Skeleton className="h-64" />;
  if (taxRates.error) return <LoadError error={taxRates.error} />;

  const rates = taxRates.data ?? [];
  const today = new Date().toISOString().slice(0, 10);

  // Editing a rate is Dev_Admin's, same as setting one (the "Set VAT rate" button lives up top with
  // "Add company"). Non-admins get the read-only table — the server enforces this too, so the Edit
  // button is hidden rather than shown-and-403ing.
  const canManage = user.data?.permissions.includes("system.dev_admin") ?? false;

  async function saveDate(effectiveFrom: string, reason: string) {
    if (editing === null) return;

    try {
      await updateTaxRateFrom(editing.id, effectiveFrom, reason, companyId);
      toast.success("Adoption date updated.");
      setEditing(null);
      await queryClient.invalidateQueries({ queryKey: ["tax-rates", companyId] });
    } catch (error: unknown) {
      toast.error(message(error));
    }
  }

  return (
    <Card>
      <SectionHeading
        title="Tax rates"
        blurb="Changing a rate affects future documents only. Every line stores the rate that applied when it was saved, so last year's invoice reprints with last year's tax — which the old system gets wrong."
      />

      <div className="mt-5 overflow-x-auto">
        <table className="w-full min-w-lg text-sm">
          <thead>
            <tr className="border-b border-subtle text-left text-xs uppercase tracking-wide text-muted">
              <th className="pb-2 font-medium">Name</th>
              <th className="pb-2 text-right font-medium">Rate</th>
              <th className="pb-2 font-medium">From</th>
              <th className="pb-2 font-medium">Until</th>
              <th className="pb-2 font-medium">Default</th>
              {canManage && <th className="pb-2" />}
            </tr>
          </thead>
          <tbody className="divide-y divide-subtle">
            {rates.map((rate) => {
              // "In force" is about the dates, not the flag: a rate that is default but does not start
              // until January is not the rate anything is being taxed at today, and showing it as though
              // it were is how somebody ends up believing a change took effect when it has not.
              const started = rate.effectiveFrom <= today;
              // `!= null` on purpose: the generated type is `string | null | undefined`, so a strict
              // `!== null` would let an absent field through as "ended".
              const ended = rate.effectiveTo != null && rate.effectiveTo < today;
              const inForce = started && !ended;

              return (
                <tr key={rate.id} className={inForce ? undefined : "text-muted"}>
                  <td className="py-2.5">{rate.name}</td>
                  <td className="py-2.5 text-right tabular">{rate.percentage}%</td>
                  <td className="py-2.5 tabular">{rate.effectiveFrom}</td>
                  <td className="py-2.5 tabular">{rate.effectiveTo ?? "—"}</td>
                  <td className="py-2.5">
                    {rate.isDefault ? (
                      <span
                        className={cn(
                          "rounded px-1.5 py-0.5 text-xs",
                          inForce
                            ? "bg-success-subtle text-success-text"
                            : "border border-subtle text-muted",
                        )}
                      >
                        {inForce ? "Default" : started ? "Ended" : "Scheduled"}
                      </span>
                    ) : (
                      <span>—</span>
                    )}
                  </td>
                  {canManage && (
                    <td className="py-2.5 text-right">
                      <Button variant="ghost" onClick={() => setEditing(rate)}>
                        Edit
                      </Button>
                    </td>
                  )}
                </tr>
              );
            })}

            {rates.length === 0 && (
              <tr>
                <td colSpan={canManage ? 6 : 5} className="py-6 text-center text-muted">
                  No tax rates configured for this company. A VAT-registered company cannot raise any
                  document until it has a default rate in force.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {canManage && (
        <p className="mt-4 text-xs text-muted">
          The rate and percentage are business-wide — change them with <strong>Set VAT rate</strong> above,
          which applies to every VAT-registered company at once. Here you can only move <em>this</em>
          company&rsquo;s adoption date, for when one entity switched over on a different day.
        </p>
      )}

      {editing && (
        <EditRateFromDialog rate={editing} onCancel={() => setEditing(null)} onSave={saveDate} />
      )}
    </Card>
  );
}

/**
 * Set the business VAT rate — the one applied to every VAT-registered company.
 *
 * The valid-from date is the point of it: it is what lets a rate change be entered when it is announced
 * rather than on the morning it takes effect. The server resolves each document against the rate in force
 * on that document's own date, so a rate dated forward simply waits, and the current rate is left alone.
 */
function SetVatRateDialog({ onCancel, onSaved }: {
  onCancel: () => void;
  onSaved: (companiesAffected: number) => void | Promise<void>;
}) {
  const [name, setName] = useState("VAT 18%");
  const [percentage, setPercentage] = useState("18");
  const [from, setFrom] = useState(new Date().toISOString().slice(0, 10));
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const percent = Number(percentage);
  const valid =
    name.trim() !== ""
    && percentage.trim() !== ""
    && Number.isFinite(percent)
    && percent >= 0
    && percent <= 100
    && from !== ""
    && reason.trim().length >= MINIMUM_REASON_LENGTH;

  async function submit() {
    setSaving(true);
    try {
      const result = await setVatRate(
        { name: name.trim(), percentage: percent, effectiveFrom: from },
        reason.trim(),
      );
      await onSaved(result.companiesAffected);
    } catch (error: unknown) {
      toast.error(message(error));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onCancel()}
      title="Set the VAT rate"
      description="Applied to every VAT-registered company at once, from the date below. The current rate stays in place for documents dated before it, so a change can be entered ahead of time."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </Button>
          <Button pending={saving} disabled={!valid} onClick={submit}>
            Apply
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} placeholder="VAT 18%" />

        <Input
          label="Rate %"
          inputMode="decimal"
          value={percentage}
          onChange={(e) => setPercentage(e.target.value)}
          placeholder="18"
        />

        <Input
          label="Applies from"
          type="date"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          hint="Documents dated on or after this use the new rate. Earlier documents keep the old one."
        />

        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
        />
      </div>
    </Dialog>
  );
}

/**
 * Move when one company adopts its rate — the only per-company tax edit.
 *
 * The name and percentage are shown but not editable: they are the business's VAT rate, the same for every
 * company, and set through <c>SetVatRate</c>. What a single company may vary is the day it starts, for the
 * case where one entity changed its systems later than the others.
 */
function EditRateFromDialog({ rate, onCancel, onSave }: {
  rate: TaxRate;
  onCancel: () => void;
  onSave: (effectiveFrom: string, reason: string) => void | Promise<void>;
}) {
  const [from, setFrom] = useState(rate.effectiveFrom);
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const valid = from !== "" && reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onCancel()}
      title={`Adoption date — ${rate.name}`}
      description="When this company started charging this rate. Documents already raised are untouched; only future ones move with it."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={saving}>
            Cancel
          </Button>
          <Button
            pending={saving}
            disabled={!valid}
            onClick={() => {
              setSaving(true);
              void Promise.resolve(onSave(from, reason.trim())).finally(() => setSaving(false));
            }}
          >
            Save
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-center justify-between gap-3 text-sm">
          <span className="text-muted">Rate</span>
          <span className="tabular text-text">{rate.name} · {rate.percentage}%</span>
        </div>

        <Input
          label="Valid from"
          type="date"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          hint="Documents dated on or after this use this rate for this company."
        />

        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
        />
      </div>
    </Dialog>
  );
}

// --- Mail ------------------------------------------------------------------------------------

function MailSection({ companyId }: { companyId: number }) {
  const queryClient = useQueryClient();
  const mail = useQuery({ queryKey: ["mail", companyId], queryFn: () => getMailSettings(companyId) });

  if (mail.isPending) return <Skeleton className="h-96" />;
  if (mail.error || !mail.data) return <LoadError error={mail.error} />;

  return (
    <MailForm
      settings={mail.data}
      onSave={(next, password, reason) =>
        saveMailSettings({ ...next, password }, reason, companyId)
          .then(() => {
            toast.success("Mail settings saved.");
            void queryClient.invalidateQueries({ queryKey: ["mail", companyId] });
          })
          .catch((error: unknown) => toast.error(message(error)))
      }
      onTest={(to) =>
        sendTestEmail(to, companyId)
          .then(() => toast.success(`Test message sent to ${to}.`))
          .catch((error: unknown) => toast.error(message(error)))
      }
    />
  );
}

function MailForm({ settings, onSave, onTest }: {
  settings: MailSettings;
  onSave: (settings: MailSettings, password: string | null, reason: string) => void;
  onTest: (to: string) => void;
}) {
  const [draft, setDraft] = useState(settings);
  const [password, setPassword] = useState("");
  const [testTo, setTestTo] = useState("");
  const [reason, setReason] = useState("");

  const set = <K extends keyof MailSettings>(key: K, value: MailSettings[K]) =>
    setDraft((d) => ({ ...d, [key]: value }));

  return (
    <Card>
      <SectionHeading
        title="Outgoing mail"
        blurb="The SMTP password is encrypted and write-only: it is never sent back to this screen. Leave it blank to keep the stored one."
      />

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <Input label="Host" value={draft.host} onChange={(e) => set("host", e.target.value)} />
        <Input
          label="Port"
          type="number"
          value={draft.port}
          onChange={(e) => set("port", Number(e.target.value))}
        />
        <Input
          label="Username"
          value={draft.username ?? ""}
          onChange={(e) => set("username", e.target.value)}
        />
        <Input
          label={settings.hasPassword ? "Password (leave blank to keep)" : "Password"}
          type="password"
          placeholder={settings.hasPassword ? "••••••" : ""}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <Input
          label="From address"
          value={draft.fromAddress ?? ""}
          onChange={(e) => set("fromAddress", e.target.value)}
        />
        <Input
          label="From name"
          value={draft.fromName ?? ""}
          onChange={(e) => set("fromName", e.target.value)}
        />
      </div>

      <div className="mt-5 rounded-lg border border-subtle bg-surface-sunken p-3">
        <label className="flex items-center gap-2 text-sm text-text">
          <input
            type="checkbox"
            className="size-4 rounded border-subtle text-primary focus-visible:ring-2 focus-visible:ring-ring/25"
            checked={draft.sendEnabled}
            onChange={(e) => set("sendEnabled", e.target.checked)}
          />
          Sending enabled
        </label>
        <p className="mt-1 text-xs text-muted">
          The kill switch. With this off nothing is sent — which is what stops a restored copy of
          production from emailing real customers from a staging server.
        </p>
      </div>

      <ReasonAndSave
        reason={reason}
        setReason={setReason}
        onSave={() => onSave(draft, password || null, reason)}
        label="Save mail settings"
      />

      <div className="mt-6 border-t border-subtle pt-5">
        <p className="text-sm font-medium text-text">Send a test message</p>
        <p className="mt-1 text-xs text-muted">
          Without this, a broken mail server is discovered when a customer says they never got their
          invoice.
        </p>
        <div className="mt-3 flex flex-wrap items-end gap-3">
          <Input
            label="To"
            type="email"
            placeholder="you@example.com"
            className="min-w-56"
            value={testTo}
            onChange={(e) => setTestTo(e.target.value)}
          />
          <Button
            variant="secondary"
            size="sm"
            disabled={!testTo.includes("@")}
            onClick={() => onTest(testTo)}
          >
            Send test
          </Button>
        </div>
      </div>
    </Card>
  );
}

// --- Shared ----------------------------------------------------------------------------------

function SectionHeading({ title, blurb }: { title: string; blurb: string }) {
  return (
    <div>
      <h2 className="font-medium text-text">{title}</h2>
      <p className="mt-1 max-w-prose text-sm text-muted">{blurb}</p>
    </div>
  );
}

function ReasonAndSave({ reason, setReason, onSave, label }: {
  reason: string;
  setReason: (value: string) => void;
  onSave: () => void;
  label: string;
}) {
  // The server rejects these without a reason; disabling the button early just avoids a round trip
  // that returns 400. It is not the enforcement — the server is.
  const ok = reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <div className="mt-6 border-t border-subtle pt-5">
      <Input
        label="Reason for this change"
        placeholder="Recorded in the audit log against your name."
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        hint={
          reason.trim().length > 0 && !ok
            ? `${MINIMUM_REASON_LENGTH - reason.trim().length} more character(s) needed.`
            : undefined
        }
      />
      <Button size="sm" className="mt-3" disabled={!ok} onClick={onSave}>
        {label}
      </Button>
    </div>
  );
}

function LoadError({ error }: { error: unknown }): ReactNode {
  const api = error instanceof ApiError ? error : null;
  return (
    <Card>
      <ErrorBanner
        message={api?.message ?? "This section could not be loaded."}
        correlationId={api?.correlationId}
      />
    </Card>
  );
}

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}

/**
 * The two-letter badge for a company: the initial of each word, not the first two characters.
 * "Smart Technologies" → "ST", "Smart Net" → "SN" — otherwise both collapse to "SM".
 * A single-word name falls back to its first two characters.
 */
function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase();
}
