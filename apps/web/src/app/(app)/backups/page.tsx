"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { ApiError } from "@/lib/api";
import { MINIMUM_REASON_LENGTH } from "@/lib/admin";
import { instantFromApi } from "@/lib/time";
import { me } from "@/lib/auth";
import {
  backupDownloadUrl,
  freshBackupDownloadUrl,
  getBackupSettings,
  listBackups,
  restoreBackup,
  restoreFromUpload,
  saveBackupSettings,
  takeBackupNow,
  type BackupSettings,
  type BackupSummary,
} from "@/lib/settings";
import { PageHeader } from "@/components/shell/app-shell";
import { Button, Card, Dialog, ErrorBanner, FadeIn, Input, Skeleton, toast } from "@/components/ui";

/**
 * Database backups — the schedule, the manual button, and the restore.
 *
 * Dev_Admin only, and the endpoints behind it are gated the same way. A backup is a copy of every record
 * the business has; downloading one is exfiltrating the company and restoring one is overwriting it.
 */
export default function BackupsPage() {
  const queryClient = useQueryClient();
  const user = useQuery({ queryKey: ["me"], queryFn: me });
  const settings = useQuery({ queryKey: ["backup-settings"], queryFn: getBackupSettings });

  // No retry. Listing means reaching somebody else's FTP server, and when that is down the default
  // policy turns one slow failure into three — the page spun for minutes and showed a skeleton the whole
  // time. Fail once, say so, and leave the rest of the screen working.
  const backups = useQuery({ queryKey: ["backups"], queryFn: listBackups, retry: false });

  const [restoring, setRestoring] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [busy, setBusy] = useState(false);

  const isDevAdmin = user.data?.permissions.includes("system.dev_admin") ?? false;
  const configured = (settings.data?.host ?? "") !== "";

  async function takeNow() {
    setBusy(true);
    try {
      const taken = await takeBackupNow("Manual backup taken from the Backups screen.");
      toast.success(`Backup taken — ${taken.name}`);
      await queryClient.invalidateQueries({ queryKey: ["backups"] });
    } catch (error: unknown) {
      toast.error(message(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <FadeIn className="space-y-6">
      <PageHeader
        title="Backups"
        description="A copy of the whole database, taken every hour and kept for the newest fifteen. Restoring one replaces every record in the system."
      />

      {settings.error && (
        <ErrorBanner
          message={(settings.error as ApiError).message}
          correlationId={(settings.error as ApiError).correlationId}
        />
      )}

      {settings.isPending ? (
        <Skeleton className="h-64" />
      ) : (
        <>
          {isDevAdmin && (
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={takeNow} pending={busy} disabled={!configured}>
                Back up now
              </Button>

              {/* A plain navigation, so the browser downloads it — the auth cookie rides along because
                  the request is same-origin in production. Works even with no destination configured,
                  which is exactly when somebody most wants a copy in their hand. */}
              <a href={freshBackupDownloadUrl()} download>
                <Button variant="secondary">Download a fresh backup</Button>
              </a>

              <Button
                variant="secondary"
                onClick={() => setUploading(true)}
                disabled={!settings.data?.restoreAvailable}
              >
                Restore from a file
              </Button>
            </div>
          )}

          {!configured && (
            <Card>
              <p className="text-sm text-muted">
                No backup destination is configured yet, so the hourly job is not running. Fill in the FTP
                details below. <strong>Download a fresh backup</strong> works regardless — it streams a
                dump straight to your browser and stores nothing.
              </p>
            </Card>
          )}

          {settings.data && !settings.data.restoreAvailable && (
            <Card>
              <p className="text-sm text-muted">
                <strong>Restore is unavailable on this deployment.</strong> It needs a database credential
                that may drop and recreate the schema, which the application&rsquo;s own user deliberately
                does not have — that restriction is what keeps the audit log append-only. Set{" "}
                <code>Backup__RestoreConnectionString</code> in the server environment file to enable it.
                Backups, downloads and the rotation are unaffected.
              </p>
            </Card>
          )}

          {/* The store being unreachable is an ordinary condition — it is somebody else's server. Say
              which server and why, and leave the settings form and "download a fresh backup" working,
              because neither needs the store and both are what you want when it is down. */}
          {backups.error ? (
            <Card>
              <ErrorBanner
                message={(backups.error as ApiError).message}
                correlationId={(backups.error as ApiError).correlationId}
              />
              <div className="mt-3 flex items-center gap-3">
                <Button variant="secondary" onClick={() => void backups.refetch()} pending={backups.isFetching}>
                  Try again
                </Button>
                <span className="text-xs text-muted">
                  Backups already taken are unaffected — this is only the listing.
                </span>
              </div>
            </Card>
          ) : (
            <BackupList
              backups={backups.data ?? []}
              pending={backups.isPending}
              canRestore={isDevAdmin && (settings.data?.restoreAvailable ?? false)}
              onRestore={setRestoring}
            />
          )}

          {isDevAdmin && (
            <DestinationForm
              settings={settings.data!}
              onSaved={async () => {
                await queryClient.invalidateQueries({ queryKey: ["backup-settings"] });
                await queryClient.invalidateQueries({ queryKey: ["backups"] });
              }}
            />
          )}
        </>
      )}

      {restoring && (
        <RestoreDialog
          title={`Restore ${restoring}`}
          onCancel={() => setRestoring(null)}
          onConfirm={async (reason) => {
            const outcome = await restoreBackup(restoring, reason);
            setRestoring(null);
            toast.success(`Restored. The copy taken first is ${outcome.safetyBackup}.`);
            await queryClient.invalidateQueries({ queryKey: ["backups"] });
          }}
        />
      )}

      {uploading && (
        <UploadRestoreDialog
          onCancel={() => setUploading(false)}
          onConfirm={async (file, reason) => {
            const outcome = await restoreFromUpload(file, reason);
            setUploading(false);
            toast.success(`Restored. The copy taken first is ${outcome.safetyBackup}.`);
            await queryClient.invalidateQueries({ queryKey: ["backups"] });
          }}
        />
      )}
    </FadeIn>
  );
}

function BackupList({ backups, pending, canRestore, onRestore }: {
  backups: BackupSummary[];
  pending: boolean;
  canRestore: boolean;
  onRestore: (name: string) => void;
}) {
  if (pending) return <Skeleton className="h-48" />;

  return (
    <Card>
      <div className="overflow-x-auto">
        <table className="w-full min-w-lg text-sm">
          <thead>
            <tr className="border-b border-subtle text-left text-xs uppercase tracking-wide text-muted">
              <th className="pb-2 font-medium">Backup</th>
              <th className="pb-2 text-right font-medium">Size</th>
              <th className="pb-2 font-medium">Taken</th>
              <th className="pb-2" />
            </tr>
          </thead>
          <tbody className="divide-y divide-subtle">
            {backups.map((backup) => (
              <tr key={backup.name}>
                <td className="py-2.5 text-text">{backup.name}</td>
                <td className="py-2.5 text-right tabular text-muted">{megabytes(backup.sizeBytes)}</td>
                <td className="py-2.5 tabular text-muted">
                  <TakenAt backup={backup} />
                </td>
                <td className="py-2.5 text-right">
                  <div className="flex justify-end gap-1">
                    <a href={backupDownloadUrl(backup.name)} download>
                      <Button variant="ghost">Download</Button>
                    </a>
                    {canRestore && (
                      <Button variant="ghost" onClick={() => onRestore(backup.name)}>
                        Restore
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}

            {backups.length === 0 && (
              <tr>
                <td colSpan={4} className="py-6 text-center text-muted">
                  No backups on the destination yet.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

/** The FTP destination. Same contract as the SMTP form: the password is write-only. */
function DestinationForm({ settings, onSaved }: {
  settings: BackupSettings;
  onSaved: () => void | Promise<void>;
}) {
  const [draft, setDraft] = useState(settings);
  const [password, setPassword] = useState("");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);

  const set = <K extends keyof BackupSettings>(key: K, value: BackupSettings[K]) =>
    setDraft((d) => ({ ...d, [key]: value }));

  const valid =
    draft.host.trim() !== ""
    && draft.remotePath.trim() !== ""
    && draft.safetyPath.trim() !== ""
    && draft.safetyPath.trim() !== draft.remotePath.trim()
    && draft.retention >= 1
    && reason.trim().length >= MINIMUM_REASON_LENGTH;

  async function save() {
    setSaving(true);
    try {
      await saveBackupSettings(
        {
          enabled: draft.enabled,
          host: draft.host.trim(),
          port: draft.port,
          username: draft.username?.trim() || null,
          password: password === "" ? null : password,
          useTls: draft.useTls,
          acceptAnyCertificate: draft.acceptAnyCertificate,
          remotePath: draft.remotePath.trim(),
          safetyPath: draft.safetyPath.trim(),
          retention: draft.retention,
        },
        reason.trim(),
      );

      setPassword("");
      setReason("");
      toast.success("Backup destination saved.");
      await onSaved();
    } catch (error: unknown) {
      toast.error(message(error));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card>
      <h2 className="text-sm font-semibold text-text">Destination</h2>
      <p className="mt-1 text-xs text-muted">
        Where the hourly backup is sent. The password is encrypted and write-only: it is never sent back to
        this screen, so leave it blank to keep the stored one.
      </p>

      <div className="mt-5 grid gap-4 sm:grid-cols-2">
        <Input label="Host" value={draft.host} onChange={(e) => set("host", e.target.value)} />
        <Input
          label="Port"
          inputMode="numeric"
          value={String(draft.port)}
          onChange={(e) => set("port", Number(e.target.value) || 0)}
        />
        <Input
          label="Username"
          value={draft.username ?? ""}
          onChange={(e) => set("username", e.target.value)}
        />
        <Input
          label="Password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder={settings.hasPassword ? "••••••••" : ""}
          hint={settings.hasPassword ? "Stored. Leave blank to keep it." : "Not set yet."}
        />
        <Input
          label="Backup folder"
          value={draft.remotePath}
          onChange={(e) => set("remotePath", e.target.value)}
        />
        <Input
          label="Pre-restore folder"
          value={draft.safetyPath}
          onChange={(e) => set("safetyPath", e.target.value)}
          hint="Must differ from the backup folder, or the rotation would delete the safety copies."
        />
        <Input
          label="Keep this many"
          inputMode="numeric"
          value={String(draft.retention)}
          onChange={(e) => set("retention", Number(e.target.value) || 0)}
          hint="Older backups are deleted after each run."
        />
      </div>

      <div className="mt-4 space-y-2">
        <Toggle
          checked={draft.enabled}
          onChange={(v) => set("enabled", v)}
          label="Take a backup every hour"
          hint="Off leaves the destination configured but the schedule stopped. Manual backups still work."
        />
        <Toggle
          checked={draft.useTls}
          onChange={(v) => set("useTls", v)}
          label="Encrypt the connection (FTPS)"
          hint="A dump contains every customer record and the plaintext password column. Without this it crosses the network in the clear."
        />
        <Toggle
          checked={draft.acceptAnyCertificate}
          onChange={(v) => set("acceptAnyCertificate", v)}
          label="Accept a self-signed certificate"
          hint="Only if the server's certificate is not trusted. It still encrypts, but it can no longer tell the real server from one impersonating it."
        />
      </div>

      <div className="mt-4">
        <Input
          label="Reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          hint={`Recorded in the audit log. At least ${MINIMUM_REASON_LENGTH} characters.`}
        />
      </div>

      <Button className="mt-4" onClick={save} pending={saving} disabled={!valid}>
        Save destination
      </Button>
    </Card>
  );
}

function Toggle({ checked, onChange, label, hint }: {
  checked: boolean;
  onChange: (value: boolean) => void;
  label: string;
  hint: string;
}) {
  return (
    <label className="flex items-start gap-2 text-sm">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-1"
      />
      <span>
        <span className="text-text">{label}</span>
        <span className="block text-xs text-muted">{hint}</span>
      </span>
    </label>
  );
}

/**
 * The confirmation a restore takes.
 *
 * Typing the word is not the security — the permission is — it is the pause. A restore replaces every
 * record in the database, and a button that does that on one click is one somebody eventually presses by
 * accident.
 */
function RestoreDialog({ title, onCancel, onConfirm }: {
  title: string;
  onCancel: () => void;
  onConfirm: (reason: string) => Promise<void>;
}) {
  const [confirm, setConfirm] = useState("");
  const [reason, setReason] = useState("");
  const [running, setRunning] = useState(false);

  const valid = confirm === "RESTORE" && reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && !running && onCancel()}
      title={title}
      description="Every record in the database will be replaced by the contents of this backup — including the audit log. A copy of the current database is taken first, automatically, and its name is shown when this finishes."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={running}>
            Cancel
          </Button>
          <Button
            pending={running}
            disabled={!valid}
            onClick={() => {
              setRunning(true);
              onConfirm(reason.trim())
                .catch((error: unknown) => toast.error(message(error)))
                .finally(() => setRunning(false));
            }}
          >
            Restore
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Input
          label="Type RESTORE to confirm"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          placeholder="RESTORE"
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

function UploadRestoreDialog({ onCancel, onConfirm }: {
  onCancel: () => void;
  onConfirm: (file: File, reason: string) => Promise<void>;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [confirm, setConfirm] = useState("");
  const [reason, setReason] = useState("");
  const [running, setRunning] = useState(false);

  const valid = file !== null && confirm === "RESTORE" && reason.trim().length >= MINIMUM_REASON_LENGTH;

  return (
    <Dialog
      open
      onOpenChange={(open) => !open && !running && onCancel()}
      title="Restore from a file"
      description="The file is run against the database as SQL. Only upload a backup this system produced — every record will be replaced by its contents. A copy of the current database is taken first."
      footer={
        <>
          <Button variant="secondary" onClick={onCancel} disabled={running}>
            Cancel
          </Button>
          <Button
            pending={running}
            disabled={!valid}
            onClick={() => {
              setRunning(true);
              onConfirm(file!, reason.trim())
                .catch((error: unknown) => toast.error(message(error)))
                .finally(() => setRunning(false));
            }}
          >
            Restore
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div>
          <input
            ref={input}
            type="file"
            accept=".gz"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="block w-full text-sm text-muted"
          />
          <p className="mt-1 text-xs text-muted">A gzipped SQL dump (.sql.gz).</p>
        </div>

        <Input
          label="Type RESTORE to confirm"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          placeholder="RESTORE"
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
 * When the backup was taken, in the reader's own time zone.
 *
 * Everything server-side is UTC, deliberately, and this is the point where that has to stop being the
 * reader's problem: 06:53 UTC is 12:23 in Colombo, and a list of backup times that is six hours off is
 * worse than no times at all — it is the wrong answer to "did the midday backup run?".
 *
 * The instant is taken from the <b>name</b> rather than the modified time the FTP server reports. The
 * name carries a fixed-width UTC stamp we wrote ourselves; FTP timestamps are server-local on some
 * daemons, minute-granular on others, and occasionally the upload time rather than the file's. The
 * rotation already trusts the name for exactly this reason. The reported time is the fallback, for a
 * file whose name somehow does not parse.
 */
function TakenAt({ backup }: { backup: BackupSummary }) {
  const instant = instantOf(backup);

  if (instant === null) {
    return <>—</>;
  }

  return (
    <time dateTime={instant.toISOString()} title={`${instant.toUTCString()} (stored as UTC)`}>
      {instant.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" })}
    </time>
  );
}

/** smartnet-auto-20260721-065300.sql.gz → the instant it names, as UTC. */
function instantOf(backup: BackupSummary): Date | null {
  const stamp = /-(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})(\d{2})\.sql\.gz$/.exec(backup.name);

  if (stamp) {
    const [, y, mo, d, h, mi, s] = stamp;
    return new Date(Date.UTC(+y, +mo - 1, +d, +h, +mi, +s));
  }

  // Same helper every other screen uses, so an instant reads the same wherever it is shown.
  return instantFromApi(backup.modifiedUtc);
}

const megabytes = (bytes: number) => `${(bytes / 1024 / 1024).toFixed(1)} MB`;

function message(error: unknown) {
  return error instanceof ApiError ? error.message : "That did not work.";
}
