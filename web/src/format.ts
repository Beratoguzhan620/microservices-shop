import type { OrderStatus } from "./types";

const lira = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 2 });

/** 1149 → "₺1.149,00": Turkish lira as a Turkish shop writes it. */
export const money = (value: number) => lira.format(value);

export function dateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
}

/**
 * A short reference for people. Order ids are sequential GUIDs whose last digits are a counter shared by
 * many orders, so no slice of the id tells orders apart; an FNV-1a hash of the whole id does.
 */
export function shortId(id: string): string {
  let hash = 0x811c9dc5;
  for (const char of id.toLowerCase()) {
    hash ^= char.charCodeAt(0);
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }
  return hash.toString(36).toUpperCase().padStart(7, "0");
}

export const statusLabel: Record<OrderStatus, string> = {
  Submitted: "Received",
  AwaitingPayment: "Awaiting payment",
  Confirmed: "Paid",
  Shipped: "Shipped",
  Rejected: "Not placed",
  Cancelled: "Cancelled",
};

/** Orders still moving on their own; the page keeps asking until they settle. */
export const isInFlight = (status: OrderStatus | undefined) => status === undefined || status === "Submitted" || status === "AwaitingPayment";

/** The steps a customer sees, and how far the order got. */
export function progress(status: OrderStatus): { steps: string[]; reached: number; failed: boolean } {
  const steps = ["Received", "Stock held", "Paid", "Shipped"];
  switch (status) {
    case "Submitted":
      return { steps, reached: 0, failed: false };
    case "AwaitingPayment":
      return { steps, reached: 1, failed: false };
    case "Confirmed":
      return { steps, reached: 2, failed: false };
    case "Shipped":
      return { steps, reached: 3, failed: false };
    default:
      return { steps, reached: 0, failed: true };
  }
}

/** Groups a card number in fours as it is typed; only digits survive. */
export function formatCard(value: string): string {
  return value.replace(/\D/g, "").slice(0, 19).replace(/(\d{4})(?=\d)/g, "$1 ");
}

/** "12/30" → [12, 2030]. */
export function parseExpiry(value: string): [number, number] | null {
  const match = /^(\d{1,2})\s*\/\s*(\d{2}|\d{4})$/.exec(value.trim());
  if (!match) return null;
  const month = Number(match[1]);
  const year = match[2].length === 2 ? 2000 + Number(match[2]) : Number(match[2]);
  return month >= 1 && month <= 12 ? [month, year] : null;
}

export const testCards = [
  { number: "4242 4242 4242 4242", outcome: "approved" },
  { number: "4000 0000 0000 0002", outcome: "declined" },
  { number: "4000 0000 0000 9995", outcome: "insufficient funds" },
  { number: "4000 0000 0000 0077", outcome: "no answer, times out" },
];
