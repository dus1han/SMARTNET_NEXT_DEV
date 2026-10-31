import { Badge } from "@/components/ui";

/** Where a proforma stands: Open (waiting to be converted), Invoiced, or Voided. */
export function ProformaStatusBadge({ status }: { status: string }) {
  const tone = status === "Invoiced" ? "success" : status === "Voided" ? "danger" : "neutral";
  return <Badge tone={tone}>{status}</Badge>;
}
