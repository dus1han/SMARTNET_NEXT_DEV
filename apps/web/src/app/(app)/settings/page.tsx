"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2, Hash, Mail, Percent, SlidersHorizontal, type LucideIcon } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ApiError } from "@/lib/api";
import {
  RULE_LABELS,
  deleteCompanyLogo,
  getBusinessRules,
  getCompany,
  getCompanyLogoUrl,
  getMailSettings,
  getTaxRates,
  listCompanies,
  saveBusinessRules,
  saveCompany,
  uploadCompanyLogo,
  saveMailSettings,
  sendTestEmail,
  type BusinessRule,
  type CompanyProfile,
  type CompanySummary,
  type MailSettings,
} from "@/lib/settings";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { cn } from "@/lib/cn";
import { Numbering } from "@/components/numbering";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

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
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });
  const [companyId, setCompanyId] = useState<number | null>(null);
  const [section, setSection] = useState<SectionKey>("company");

  const active = companyId ?? companies.data?.[0]?.id ?? null;

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
          <CompanyPicker
            companies={companies.data ?? []}
            active={active}
            onChange={setCompanyId}
          />

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
  const taxRates = useQuery({
    queryKey: ["tax-rates", companyId],
    queryFn: () => getTaxRates(companyId),
  });

  if (taxRates.isPending) return <Skeleton className="h-64" />;
  if (taxRates.error) return <LoadError error={taxRates.error} />;

  return (
    <Card>
      <SectionHeading
        title="Tax rates"
        blurb="Changing a rate affects future documents only. Every line stores the rate that applied when it was saved, so last year's invoice reprints with last year's tax — which the old system gets wrong."
      />

      <div className="mt-5 overflow-x-auto">
        <table className="w-full min-w-md text-sm">
          <thead>
            <tr className="border-b border-subtle text-left text-xs uppercase tracking-wide text-muted">
              <th className="pb-2 font-medium">Name</th>
              <th className="pb-2 text-right font-medium">Rate</th>
              <th className="pb-2 font-medium">From</th>
              <th className="pb-2 font-medium">Default</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-subtle">
            {taxRates.data?.map((rate) => (
              <tr key={rate.id}>
                <td className="py-2.5 text-text">{rate.name}</td>
                <td className="py-2.5 text-right tabular text-text">{rate.percentage}%</td>
                <td className="py-2.5 tabular text-muted">{rate.effectiveFrom}</td>
                <td className="py-2.5">
                  {rate.isDefault ? (
                    <span className="rounded bg-success-subtle px-1.5 py-0.5 text-xs text-success-text">
                      Default
                    </span>
                  ) : (
                    <span className="text-muted">—</span>
                  )}
                </td>
              </tr>
            ))}

            {taxRates.data?.length === 0 && (
              <tr>
                <td colSpan={4} className="py-6 text-center text-muted">
                  No tax rates configured for this company.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <p className="mt-4 text-xs text-muted">
        Tax rates are seeded per company and edited in a later slice. This view is read-only for now.
      </p>
    </Card>
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
