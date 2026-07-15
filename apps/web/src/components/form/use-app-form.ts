"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import {
  useForm,
  type DefaultValues,
  type FieldValues,
  type Path,
  type UseFormReturn,
} from "react-hook-form";
import type { ZodType } from "zod";
import { ApiError } from "@/lib/api";

/**
 * A form, wired to a zod schema, that knows what to do with the server's answer.
 *
 * <b>One schema, two jobs</b> (DEVELOPMENT.md §1): it validates in the browser and it types the
 * payload. The server re-validates all of it regardless — the server is the authority, and a rule
 * only the client enforces is a rule that a direct API call ignores.
 */
export function useAppForm<T extends FieldValues>(
  schema: ZodType<T>,
  defaultValues: DefaultValues<T>,
): UseFormReturn<T> {
  return useForm<T>({
    resolver: zodResolver(schema as never),
    defaultValues,

    // Validate on first blur, then live. Validating from the first keystroke means a field shouts
    // "required" at somebody who has simply not finished typing their name yet.
    mode: "onTouched",
  });
}

/**
 * Puts the server's validation errors onto the fields they belong to.
 *
 * The API returns RFC 9457 ValidationProblemDetails: `{ errors: { NewPassword: ["too short"] } }`.
 * Dumping that into a banner and leaving the fields unmarked is the difference between "something
 * was wrong" and "this field, this reason" — and on a twenty-field invoice form the first is
 * useless.
 *
 * <p>C# property names are PascalCase and form fields are camelCase, so keys are matched
 * case-insensitively rather than by hope.</p>
 *
 * @returns the errors that could NOT be attached to a field — show these in a banner. A rejection
 *   nobody can see is a form that appears to do nothing when you press Save.
 */
export function applyServerErrors<T extends FieldValues>(
  form: UseFormReturn<T>,
  error: unknown,
): string[] {
  if (!(error instanceof ApiError)) {
    return ["Something went wrong. Please try again."];
  }

  const fieldErrors = error.fieldErrors;

  if (!fieldErrors || Object.keys(fieldErrors).length === 0) {
    return [error.message];
  }

  const fields = Object.keys(form.getValues());
  const unattached: string[] = [];

  for (const [key, messages] of Object.entries(fieldErrors)) {
    const message = messages[0];
    if (!message) continue;

    const match = fields.find((field) => field.toLowerCase() === key.toLowerCase());

    if (match) {
      form.setError(match as Path<T>, { type: "server", message });
    } else {
      // A header (X-Change-Reason), or a field this form does not have. It still has to be said.
      unattached.push(message);
    }
  }

  return unattached;
}
