"use client";

import { Eye, EyeOff } from "lucide-react";
import {
  useId,
  useState,
  type ChangeEvent,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
} from "react";
import { cn } from "@/lib/cn";

// A partial decimal: optional leading minus, digits, at most one dot. Empty is allowed (a cleared field).
const DECIMAL = /^-?\d*\.?\d*$/;

const control = cn(
  "w-full rounded-md border border-subtle bg-surface px-3 py-2 text-sm text-text",
  "placeholder:text-muted",
  "transition-colors duration-200 ease-out",
  "focus-visible:border-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/25",
  "disabled:cursor-not-allowed disabled:opacity-60",
  // Driven by aria-invalid rather than a `error` class, so the visual state and the state a screen
  // reader announces can never disagree.
  "aria-invalid:border-danger aria-invalid:focus-visible:ring-danger/25",
);

interface FieldShellProps {
  label: string;
  /** The server's message for this field, or the client's. Same treatment either way. */
  error?: string;
  hint?: ReactNode;
  required?: boolean;
  children: (props: { id: string; describedBy?: string; invalid: boolean }) => ReactNode;
}

/**
 * The label / control / error / hint arrangement, in one place.
 *
 * The wiring is the point. `aria-describedby` links the error to the input, so a screen reader
 * reads "VAT number, invalid, must be 9 digits" rather than leaving a blind user with red text they
 * will never hear. Getting that right once here is why the other thirty-nine forms get it right.
 */
function FieldShell({ label, error, hint, required, children }: FieldShellProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;

  const describedBy = [error ? errorId : null, hint ? hintId : null]
    .filter(Boolean)
    .join(" ") || undefined;

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-text">
        {label}
        {required && (
          <span className="ml-0.5 text-danger" aria-hidden>
            *
          </span>
        )}
      </label>

      {children({ id, describedBy, invalid: Boolean(error) })}

      {/* aria-live: a validation error that appears after a failed submit must be announced, not
          just drawn. */}
      {error && (
        <p id={errorId} role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}

      {hint && !error && (
        <p id={hintId} className="text-sm text-muted">
          {hint}
        </p>
      )}
    </div>
  );
}

export type InputProps = Omit<InputHTMLAttributes<HTMLInputElement>, "id"> & {
  label: string;
  error?: string;
  hint?: ReactNode;
  /** Force numeric-only entry (also implied by inputMode="decimal"). Non-decimal keystrokes/pastes are rejected. */
  numeric?: boolean;
};

export function Input({ label, error, hint, className, required, numeric, inputMode, onChange, ...props }: InputProps) {
  // A value field only accepts a value. Any input flagged numeric (or with a decimal keypad) rejects
  // anything that is not a partial decimal — typed or pasted — so "total", "qty", "VAT" et al. cannot hold
  // letters. This covers every money/quantity field in the app from the one component (see AGENTS.md).
  const isNumeric = numeric || inputMode === "decimal";
  const handleChange =
    isNumeric && onChange
      ? (e: ChangeEvent<HTMLInputElement>) => {
          if (DECIMAL.test(e.target.value)) onChange(e);
        }
      : onChange;

  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <input
          id={id}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          required={required}
          inputMode={isNumeric ? "decimal" : inputMode}
          onChange={handleChange}
          className={cn(control, className)}
          {...props}
        />
      )}
    </FieldShell>
  );
}

export type PasswordInputProps = Omit<InputProps, "type">;

/**
 * A password field that can be read back.
 *
 * Not decoration: the passwords this system issues are machine-generated and single-use
 * (`TemporaryPassword.Generate`), and a person copying one off a note into a masked field with no
 * way to check it will mistype it, get locked out after five attempts, and ring somebody. The
 * reveal is the cheapest possible fix for that, and it is under the user's control — which is the
 * only place a decision about who can see their screen belongs.
 */
export function PasswordInput({ label, error, hint, className, required, ...props }: PasswordInputProps) {
  const [revealed, setRevealed] = useState(false);

  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <div className="relative">
          <input
            id={id}
            type={revealed ? "text" : "password"}
            aria-invalid={invalid || undefined}
            aria-describedby={describedBy}
            required={required}
            className={cn(control, "pr-10", className)}
            {...props}
          />

          <button
            type="button"
            // The state, not the action: a screen reader user needs to know whether their password
            // is currently on display, which "Show password" does not tell them.
            aria-pressed={revealed}
            aria-label={revealed ? "Hide password" : "Show password"}
            onClick={() => setRevealed((current) => !current)}
            className={cn(
              "absolute inset-y-0 right-0 grid w-10 place-items-center rounded-r-md text-muted",
              "transition-colors duration-200 ease-out hover:text-text",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/25",
            )}
          >
            {revealed ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
          </button>
        </div>
      )}
    </FieldShell>
  );
}

export type TextareaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, "id"> & {
  label: string;
  error?: string;
  hint?: ReactNode;
};

export function Textarea({ label, error, hint, className, required, ...props }: TextareaProps) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <textarea
          id={id}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          required={required}
          className={cn(control, "min-h-24 resize-y", className)}
          {...props}
        />
      )}
    </FieldShell>
  );
}

export type SelectProps = Omit<SelectHTMLAttributes<HTMLSelectElement>, "id"> & {
  label: string;
  error?: string;
  hint?: ReactNode;
};

/**
 * A dropdown, wired to the same label / error / hint shell as every other field.
 *
 * A native <c>&lt;select&gt;</c> on purpose: it is accessible for free, works with the keyboard and
 * the screen reader without a line of JavaScript, and on a phone opens the platform's own picker —
 * which is the one thing a custom dropdown can never quite match. The design system's job here is to
 * make it look like it belongs, not to replace it.
 */
export function Select({ label, error, hint, className, required, children, ...props }: SelectProps) {
  return (
    <FieldShell label={label} error={error} hint={hint} required={required}>
      {({ id, describedBy, invalid }) => (
        <select
          id={id}
          aria-invalid={invalid || undefined}
          aria-describedby={describedBy}
          required={required}
          className={cn(control, "appearance-none bg-no-repeat pr-9", className)}
          style={{
            // A chevron, as a data URI, so the control needs no icon component and no extra element.
            backgroundImage:
              "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='%2364748b' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='m6 9 6 6 6-6'/%3E%3C/svg%3E\")",
            backgroundPosition: "right 0.65rem center",
          }}
          {...props}
        >
          {children}
        </select>
      )}
    </FieldShell>
  );
}

export type CheckboxProps = Omit<InputHTMLAttributes<HTMLInputElement>, "type"> & {
  label: string;
  hint?: ReactNode;
};

export function Checkbox({ label, hint, className, ...props }: CheckboxProps) {
  const id = useId();

  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2">
        <input
          id={id}
          type="checkbox"
          className={cn(
            "size-4 rounded border-subtle text-primary",
            "focus-visible:ring-2 focus-visible:ring-ring/25",
            className,
          )}
          {...props}
        />
        <label htmlFor={id} className="text-sm text-text">
          {label}
        </label>
      </div>

      {hint && <p className="ml-6 text-sm text-muted">{hint}</p>}
    </div>
  );
}
