"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ApiError } from "@/lib/api";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { me } from "@/lib/auth";
import { createCompany, listCompanies, type CompanyCreated } from "@/lib/settings";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

/**
 * The trading entities the business invoices under.
 *
 * Its own screen under Administration rather than a button on Settings, because Settings configures
 * *one* company at a time — its letterhead, its numbering, its mail — while adding one changes what the
 * business is. Dev_Admin only, and the endpoint behind it says so too: hiding a button is a courtesy,
 * the endpoint is the lock (ISSUES A5).
 */
export default function CompaniesPage() {
  const queryClient = useQueryClient();
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const companies = useQuery({ queryKey: ["companies"], queryFn: listCompanies });

  const [adding, setAdding] = useState(false);

  const isDevAdmin = user.data?.permissions.includes("system.dev_admin") ?? false;

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Companies"
        description="The trading entities the business invoices under. Each has its own numbering, letterhead and mail, configured under Settings."
      />

      {companies.error && (
        <ErrorBanner
          message={(companies.error as ApiError).message}
          correlationId={(companies.error as ApiError).correlationId}
        />
      )}

      {isDevAdmin && (
        <div>
          <Button variant="secondary" onClick={() => setAdding(true)}>
            Add company
          </Button>
        </div>
      )}

      {companies.isPending ? (
        <Skeleton className="h-48" />
      ) : (
        <Card>
          <div className="overflow-x-auto">
            <table className="w-full min-w-md text-sm">
              <thead>
                <tr className="border-b border-subtle text-left text-xs uppercase tracking-wide text-muted">
                  <th className="pb-2 font-medium">Company</th>
                  <th className="pb-2 font-medium">VAT</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {(companies.data ?? []).map((company) => (
                  <tr key={company.id}>
                    <td className="py-2.5 text-text">{company.name}</td>
                    <td className="py-2.5">
                      {company.isVatRegistered ? (
                        <span className="rounded bg-primary-ghost px-1.5 py-0.5 text-xs text-primary">
                          Registered
                        </span>
                      ) : (
                        <span className="text-muted">Not registered</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p className="mt-4 text-xs text-muted">
            Everyone who uses the system can work in every company — the company on a document says which
            entity issued it, and is not a wall between two sets of people. The rate a registered company
            charges is set under VAT rate.
          </p>
        </Card>
      )}

      {adding && (
        <AddCompanyDialog
          onCancel={() => setAdding(false)}
          onCreated={async () => {
            setAdding(false);
            await queryClient.invalidateQueries({ queryKey: ["companies"] });
          }}
        />
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
 * No VAT rate is asked for. A VAT-registered company inherits the rate the others charge today; VAT is
 * not a per-company figure to re-type. The rest of the profile (address, bank, logo, VAT number) is
 * edited under Settings a moment later.
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
      toast.error(error instanceof ApiError ? error.message : "That did not work.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && onCancel()}
      title="Add a company"
      description="Another trading entity, with its own document numbering and letterhead. Everyone who uses the system will be able to work in it."
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
              taxed at 0% and carries a zero rate only. The VAT number is added afterwards, under Settings.
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
