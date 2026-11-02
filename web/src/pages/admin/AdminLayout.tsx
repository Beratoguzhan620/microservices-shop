import type { ReactNode } from "react";
import { linkProps, type Route } from "../../router";

export function AdminLayout({ route, children }: { route: Route; children: ReactNode }) {
  const tab = (href: string, label: string, active: boolean) => (
    <a className={`tab${active ? " active" : ""}`} aria-current={active ? "page" : undefined} {...linkProps(href)}>{label}</a>
  );
  return (
    <div className="container">
      <div className="row admin-head">
        <h1 className="page-title grow" style={{ margin: 0 }}>Admin</h1>
        <nav className="tabs" aria-label="Admin">
          {tab("/admin/orders", "Orders", route.page === "admin-orders" || route.page === "admin-order")}
          {tab("/admin/products", "Products", route.page === "admin-products")}
          {tab("/admin/stock", "Stock", route.page === "admin-stock")}
        </nav>
      </div>
      {children}
    </div>
  );
}
