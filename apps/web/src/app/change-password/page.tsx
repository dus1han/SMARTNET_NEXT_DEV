"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { MINIMUM_PASSWORD_LENGTH, changePassword } from "@/lib/auth";
import { Button, Card, CardHeader, ErrorBanner, Input } from "@/components/ui";

export default function ChangePasswordPage() {
  const router = useRouter();

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // Checked here only so the user is not made to wait for a round trip to learn they mistyped.
  // The server is the authority on everything else, and re-checks all of it.
  const mismatch = confirmPassword.length > 0 && newPassword !== confirmPassword;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();

    if (mismatch) return;

    setPending(true);
    setError(null);

    try {
      await changePassword(currentPassword, newPassword);

      // The server has cleared the session: the old token still says must_change_password. So the
      // user signs in again, with the password they just chose.
      router.push("/login");
      router.refresh();
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? caught
          : new ApiError(0, "Could not reach the server. Check your connection."),
      );
    } finally {
      setPending(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-6">
      <Card className="w-full max-w-sm">
        <CardHeader
          title="Choose a new password"
          description="Your current password was stored in plain text and must be replaced before you can continue."
        />
        <form onSubmit={onSubmit} className="space-y-4">
          {error && (
            <ErrorBanner
              message={error.fieldErrors?.NewPassword?.[0] ?? error.message}
              correlationId={error.correlationId}
            />
          )}

          <Input
            label="Current password"
            name="currentPassword"
            type="password"
            autoComplete="current-password"
            required
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
          />

          <Input
            label="New password"
            name="newPassword"
            type="password"
            autoComplete="new-password"
            required
            minLength={MINIMUM_PASSWORD_LENGTH}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
          />

          <Input
            label="Confirm new password"
            name="confirmPassword"
            type="password"
            autoComplete="new-password"
            required
            error={mismatch ? "The two passwords do not match." : undefined}
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
          />

          <p className="text-sm text-neutral-600 dark:text-neutral-400">
            At least {MINIMUM_PASSWORD_LENGTH} characters. Length matters far more than
            punctuation, so a short phrase you can remember beats a short word with a symbol in it.
          </p>

          <Button type="submit" pending={pending} disabled={mismatch}>
            Change password
          </Button>
        </form>
      </Card>
    </main>
  );
}
