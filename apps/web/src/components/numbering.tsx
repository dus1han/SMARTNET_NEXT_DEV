"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import {
  PREFIX_TOKENS,
  getNumbering,
  initialiseNumbering,
  previewNumber,
  saveSeries,
  type DocumentSeries,
  type SeriesInitialisation,
} from "@/lib/settings";
import { Button, Card, Input } from "@/components/ui";

/**
 * Document numbering.
 *
 * Two things live here, and they are different in kind. The prefix is a setting an administrator
 * changes whenever they like. The counter is not: it continues from the last number the legacy app
 * issued, and it is not editable, because typing a number into a form is how somebody reissues
 * invoice 1200 by accident.
 */
export function Numbering({ companyId, onError, onDone }: {
  companyId: number;
  onError: (error: unknown) => void;
  onDone: (message: string) => void;
}) {
  const queryClient = useQueryClient();
  const series = useQuery({
    queryKey: ["numbering", companyId],
    queryFn: () => getNumbering(companyId),
  });

  const [reason, setReason] = useState("");
  const [dryRun, setDryRun] = useState<SeriesInitialisation[] | null>(null);

  const reasonOk = reason.trim().length >= MINIMUM_REASON_LENGTH;
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["numbering", companyId] });

  return (
    <Card>
      <h2 className="font-medium">Document numbering</h2>
      <p className="mt-1 text-xs text-muted">
        The prefix is yours to change. The counter is not editable — it continues from the last
        number the old system issued.
      </p>

      <div className="mt-4 space-y-4">
        {series.data?.map((s) => (
          <SeriesRow
            key={s.id}
            series={s}
            onSave={(prefix, padding, why) =>
              saveSeries(s.id, prefix, padding, why, companyId, s.rowVersion)
                .then(() => {
                  onDone(`${s.docType} numbering saved.`);
                  void refresh();
                })
                .catch(onError)
            }
          />
        ))}

        {series.data?.length === 0 && (
          <p className="text-sm text-warning-text">
            No series configured. Documents cannot be issued until you initialise numbering below —
            deliberately, so that nothing silently restarts at 1 and reissues numbers already
            printed on your invoices.
          </p>
        )}
      </div>

      <div className="mt-6 border-t border-black/5 pt-4 dark:border-white/10">
        <h3 className="text-sm font-medium">Initialise from the old system</h3>
        <p className="mt-1 text-xs text-muted">
          Reads the last number used for each document type and points the counter at the next one.
          Preview it first. Run it for real at go-live, <strong>after</strong> the old system stops
          issuing that document — anything it raises afterwards would take a number this system also
          thinks is free.
        </p>

        <div className="mt-3">
          <Input
            label="Reason"
            placeholder="Recorded in the audit log against your name."
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        </div>

        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            variant="secondary"
            size="sm"
            disabled={!reasonOk}
            onClick={() => initialiseNumbering(false, reason, companyId).then(setDryRun).catch(onError)}
          >
            Preview
          </Button>

          {/* Apply stays disabled until a preview has been run. A numbering change nobody looked
              at first is not a change anybody should be making. */}
          <Button
            size="sm"
            disabled={!reasonOk || dryRun === null}
            onClick={() =>
              initialiseNumbering(true, reason, companyId)
                .then((result) => {
                  setDryRun(result);
                  onDone("Numbering initialised from the old system.");
                  void refresh();
                })
                .catch(onError)
            }
          >
            Apply
          </Button>
        </div>

        {/* Apply is disabled until a preview has been run. A numbering change nobody looked at
            first is not a change anybody should be making. */}
        {dryRun && (
          <table className="mt-4 w-full text-sm">
            <thead className="text-left text-muted">
              <tr>
                <th className="pb-2 font-medium">Company</th>
                <th className="pb-2 font-medium">Document</th>
                <th className="pb-2 font-medium">Last used</th>
                <th className="pb-2 font-medium">Next will be</th>
              </tr>
            </thead>
            <tbody>
              {dryRun.map((row) => (
                <tr
                  key={`${row.companyId}-${row.docType}`}
                  className="border-t border-subtle"
                >
                  <td className="py-2">{row.companyId}</td>
                  <td className="py-2">{row.docType}</td>
                  <td className="py-2 tabular-nums">{row.lastIssued ?? "—"}</td>
                  <td className="py-2 font-mono">{row.example}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </Card>
  );
}

function SeriesRow({ series, onSave }: {
  series: DocumentSeries;
  onSave: (prefix: string, padding: number, reason: string) => void;
}) {
  const [prefix, setPrefix] = useState(series.prefix);
  const [padding, setPadding] = useState(series.padding);
  const [reason, setReason] = useState("");
  const [preview, setPreview] = useState<{ now: string; nextMonth: string } | null>(null);

  const reasonOk = reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <div className="rounded-lg border border-subtle p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-medium">{series.docType}</h3>
        <span className="font-mono text-sm">{series.example}</span>
      </div>

      <div className="mt-3 grid gap-3 sm:grid-cols-3">
        <Input
          label="Prefix"
          value={prefix}
          onChange={(e) => {
            setPrefix(e.target.value);
            setPreview(null);
          }}
        />
        <Input
          label="Zero-padding"
          type="number"
          value={padding}
          onChange={(e) => {
            setPadding(Number(e.target.value));
            setPreview(null);
          }}
        />
        <Input label="Next number (not editable)" value={series.nextNumber} disabled readOnly />
      </div>

      <p className="mt-2 text-xs text-muted">
        Tokens:{" "}
        {PREFIX_TOKENS.map((t) => (
          <span key={t.token} className="mr-3">
            <code>{t.token}</code> {t.meaning}
          </span>
        ))}
      </p>

      <div className="mt-3">
        <Input
          label="Reason for this change"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() =>
            previewNumber(prefix, series.nextNumber, padding).then(setPreview).catch(() => {})
          }
          className="rounded-lg border border-black/15 px-3 py-1.5 text-sm dark:border-white/20"
        >
          Preview
        </button>

        <Button size="sm" disabled={!reasonOk} onClick={() => onSave(prefix, padding, reason)}>Save</Button>

        {/* Both months are shown, so a prefix that rolls over — and one that silently does not —
            are equally visible before anyone commits to it. */}
        {preview && (
          <span className="text-sm text-muted">
            now <code className="font-mono">{preview.now}</code> · next month{" "}
            <code className="font-mono">{preview.nextMonth}</code>
          </span>
        )}
      </div>
    </div>
  );
}
