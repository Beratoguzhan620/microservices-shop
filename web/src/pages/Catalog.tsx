import { Search } from "lucide-react";
import { useEffect, useState } from "react";
import { api } from "../api";
import { money } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Availability, ProductSort } from "../types";
import { StockNote } from "../components/Badges";
import { ProductArt } from "../components/ProductArt";
import { Empty, Failed } from "../components/States";

const sorts: { value: ProductSort; label: string }[] = [
  { value: "Newest", label: "Newest" },
  { value: "PriceAsc", label: "Price, low to high" },
  { value: "PriceDesc", label: "Price, high to low" },
  { value: "Name", label: "Name" },
];

export function Catalog({ category, q }: { category?: string; q?: string }) {
  const [text, setText] = useState(q ?? "");
  const [sort, setSort] = useState<ProductSort>("Newest");
  const search = useDebounced(text);
  const [categories] = useLoad(() => api.categories(), []);
  const [products, reload] = useLoad(() => api.products({ q: search, category, sort, pageSize: 60 }), [search, category, sort]);
  const [stock, setStock] = useState<Record<string, number>>({});

  // Keep the address in step with the search, so a search can be shared or reloaded.
  useEffect(() => {
    const params = new URLSearchParams();
    if (category) params.set("category", category);
    if (search.trim()) params.set("q", search.trim());
    const target = `/${params.toString() ? `?${params}` : ""}`;
    if (target !== location.pathname + location.search) history.replaceState(null, "", target);
  }, [search, category]);

  useEffect(() => {
    const ids = products.data?.items.map((p) => p.id) ?? [];
    api.availability(ids).then(
      (list: Availability[]) => setStock(Object.fromEntries(list.map((a) => [a.productId, a.available]))),
      () => setStock({}),
    );
  }, [products.data]);

  const active = categories.data?.find((c) => c.slug === category);
  const pick = (slug?: string) => navigate(slug ? `/?category=${slug}${search ? `&q=${encodeURIComponent(search)}` : ""}` : `/${search ? `?q=${encodeURIComponent(search)}` : ""}`);

  return (
    <div className="container">
      <section className="hero">
        <h1>{active ? active.name : "Coffee, tea and the tools to brew them"}</h1>
        <p className="muted">Freshly roasted beans, brewing gear and loose-leaf tea, shipped from Samsun.</p>
      </section>

      <div className="filters">
        <div className="chips" role="tablist" aria-label="Categories">
          <button role="tab" aria-selected={!category} className={`chip${!category ? " active" : ""}`} onClick={() => pick()}>All</button>
          {categories.data?.map((c) => (
            <button key={c.id} role="tab" aria-selected={category === c.slug} className={`chip${category === c.slug ? " active" : ""}`} onClick={() => pick(c.slug)}>
              {c.name} <span className="muted small">{c.productCount}</span>
            </button>
          ))}
        </div>
        <div className="row">
          <label className="search">
            <Search size={15} />
            <span className="sr-only">Search products</span>
            <input className="input" placeholder="Search coffee, tea, gear" value={text} onChange={(e) => setText(e.target.value)} />
          </label>
          <select className="select" aria-label="Sort" value={sort} onChange={(e) => setSort(e.target.value as ProductSort)}>
            {sorts.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </div>
      </div>

      {products.state === "failed" && !products.data && <Failed message={products.error} onRetry={reload} />}
      {!products.data && products.state === "loading" && (
        <div className="grid" aria-busy="true">
          {Array.from({ length: 8 }, (_, i) => <div key={i} className="card product skeleton-card" />)}
        </div>
      )}
      {products.data?.items.length === 0 && (
        <Empty title="Nothing matches" icon={<Search size={22} />}>
          <button className="link" onClick={() => { setText(""); pick(); }}>Show everything</button>
        </Empty>
      )}
      {products.data && products.data.items.length > 0 && (
        <div className="grid" style={{ opacity: products.state === "loading" ? 0.6 : 1 }}>
          {products.data.items.map((p) => (
            <a key={p.id} className="card product" {...linkProps(`/products/${p.slug}`)} data-product={p.name}>
              <ProductArt category={p.category.slug} />
              <div className="product-body">
                <span className="small muted">{p.category.name}</span>
                <span className="product-name">{p.name}</span>
                <div className="row" style={{ justifyContent: "space-between", marginTop: "auto" }}>
                  <span className="price">{money(p.price)}</span>
                  <StockNote available={stock[p.id]} />
                </div>
              </div>
            </a>
          ))}
        </div>
      )}
    </div>
  );
}
