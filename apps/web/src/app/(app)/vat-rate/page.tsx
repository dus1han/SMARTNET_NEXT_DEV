"use client";

import { useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { me } from "@/lib/auth";
import { getTaxRates, listCompanies, setVatRate, type TaxRate } from "@/lib/settings";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

/**
 * The VAT rate the business charges.
 *
 * Its own screen under Administration, because VAT is a national rate rather than a company setting: it
 * changes for every registered entity on the same day, so it is set once here and fanned out. Settings
 * keeps only what belongs to a single company — including, for a rate change, the date that company
 * adopted it, which is the one part that may differ.
 *
 * Dev_Admin only, and enforced server-side as well as hidden here.
 */
export default function VatRatePage() {
  const queryClient = useQueryClient();
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });

  const [setting, setSetting] = useState(false);

  // One query per company. There are two of them, and the alternative is an endpoint that exists only
  // to serve this screen.
  const rateQueries = useQueries({
    queries: (companies.data ?? []).map((company) => ({
      queryKey: ["tax-rates", company.id],
      queryFn: () => getTaxRates(company.id),
    })),
  });

  const isDevAdmin = user.data?.permissions.includes("system.dev_admin") ?? false;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="VAT rate"
        description="What each company charges today, and what it will charge if a change has already been entered. Setting a rate applies it to every VAT-registered company at once."
      />

      {companies.error && (
        <ErrorBanner
          message={(companies.error as ApiError).message}
          correlationId={(companies.error as ApiError).correlationId}
        />
      )}

      {isDevAdmin && (
        <div>
          <Button variant="secondary" onClick={() => setSetting(true)}>
            Set VAT rate
          </Button>
        </div>
      )}

      {companies.isPending ? (
        <Skeleton className="h-48" />
      ) : (
        <Card>
          <div className="overflow-x-auto">
            <table className="w-full min-w-lg text-sm">
              <thead>
                <tr className="border-b border-subtle text-left text-xs uppercase tracking-wide text-muted">
                  <th className="pb-2 font-medium">Company</th>
                  <th className="pb-2 font-medium">Charging now</th>
                  <th className="pb-2 font-medium">From</th>
                  <th className="pb-2 font-medium">Scheduled</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {(companies.data ?? []).map((company, index) => {
                  const query = rateQueries[index];
                  const now = rateInForce(query?.data);
                  const next = scheduledRate(query?.data);

                  return (
                    <tr key={company.id}>
                      <td className="py-2.5 text-text">
                        {company.name}
                        {!company.isVatRegistered && (
                          <span className="ml-2 text-xs text-muted">not registered</span>
                        )}
                      </td>
                      <td className="py-2.5 tabular text-text">
                        {query?.isPending ? "…" : now ? `${now.name} · ${now.percentage}%` : "—"}
                      </td>
                      <td className="py-2.5 tabular text-muted">{now?.effectiveFrom ?? "—"}</td>
                      <td className="py-2.5 tabular text-muted">
                        {next ? `${next.percentage}% from ${next.effectiveFrom}` : "—"}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <p className="mt-4 text-xs text-muted">
            An unregistered company is taxed at 0% whatever its rates say, so it carries a zero rate only.
            A scheduled rate does not disturb the one in force — documents are taxed at the rate that
            applied on their own date. If one company adopted a rate on a different day, move that date
            under Settings → Tax rates for that company.
          </p>
        </Card>
      )}

      {setting && (
        <SetVatRateDialog
          onCancel={() => setSetting(false)}
          onSaved={async (companiesAffected) => {
            setSetting(false);
            toast.success(
              companiesAffected === 1
                ? "VAT rate set for the VAT-registered company."
                : `VAT rate set across ${companiesAffected} VAT-registered companies.`,
            );
            await queryClient.invalidateQueries({ queryKey: ["tax-rates"] });
          }}
        />
      )}
    </FadeIn>
  );
}

const today = () => new Date().toISOString().slice(0, 10);

/**
 * The rate a company is charging today — the default with the latest start on or before today.
 *
 * The same rule the tax engine applies, so this screen cannot claim one rate while documents are taxed
 * at another. Note `!= null` on `effectiveTo`: the generated type is `string | null | undefined`.
 */
function rateInForce(rates: TaxRate[] | undefined): TaxRate | null {
  const now = today();

  return (rates ?? [])
    .filter((r) => r.isDefault && r.effectiveFrom <= now && (r.effectiveTo == null || r.effectiveTo >= now))
    .sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0] ?? null;
}

/** A default that has not started yet — a rate change already entered, waiting for its date. */
function scheduledRate(rates: TaxRate[] | undefined): TaxRate | null {
  const now = today();

  return (rates ?? [])
    .filter((r) => r.isDefault && r.effectiveFrom > now)
    .sort((a, b) => a.effectiveFrom.localeCompare(b.effectiveFrom))[0] ?? null;
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
  const [from, setFrom] = useState(today());
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
      toast.error(error instanceof ApiError ? error.message : "That did not work.");
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
