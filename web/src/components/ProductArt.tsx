import { Bean, Coffee, Filter, Leaf, Package, Scale } from "lucide-react";

/** A drawn tile per category, in place of product photos the demo does not have. */
const art: Record<string, { icon: typeof Bean; bg: string; fg: string }> = {
  kahve: { icon: Bean, bg: "#F1E9DF", fg: "#6E4B2A" },
  demleme: { icon: Filter, bg: "#E6F0EE", fg: "#1F6F6A" },
  ekipman: { icon: Scale, bg: "#EAEDF1", fg: "#3B4C63" },
  cay: { icon: Leaf, bg: "#EEF1E4", fg: "#56702E" },
};

export function ProductArt({ category, size = 56, compact = false }: { category: string; size?: number; compact?: boolean }) {
  const { icon: Icon, bg, fg } = art[category] ?? { icon: category ? Package : Coffee, bg: "#EFEDE8", fg: "#6B6862" };
  return (
    <div className={`art${compact ? " compact" : ""}`} style={{ background: bg, color: fg }} aria-hidden="true">
      <Icon size={size} strokeWidth={1.4} />
    </div>
  );
}
