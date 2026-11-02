import { useEffect, useState } from "react";

export type Route =
  | { page: "catalog"; category?: string; q?: string }
  | { page: "product"; slug: string }
  | { page: "basket" }
  | { page: "checkout" }
  | { page: "orders" }
  | { page: "order"; id: string }
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "admin-orders" }
  | { page: "admin-order"; id: string }
  | { page: "admin-products" }
  | { page: "admin-stock" }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function parseRoute(pathname: string, search: string): Route {
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const query = new URLSearchParams(search);
  const [first, second, third] = parts;
  const next = query.get("next") ?? undefined;

  if (parts.length === 0) return { page: "catalog", category: query.get("category") ?? undefined, q: query.get("q") ?? undefined };
  if (first === "products" && parts.length === 2) return { page: "product", slug: second };
  if (parts.length === 1 && first === "basket") return { page: "basket" };
  if (parts.length === 1 && first === "checkout") return { page: "checkout" };
  if (parts.length === 1 && first === "orders") return { page: "orders" };
  if (first === "orders" && parts.length === 2 && guid.test(second)) return { page: "order", id: second };
  if (parts.length === 1 && first === "signin") return { page: "signin", next };
  if (parts.length === 1 && first === "register") return { page: "register", next };
  if (first === "admin") {
    if (parts.length === 1 || (parts.length === 2 && second === "orders")) return { page: "admin-orders" };
    if (second === "orders" && parts.length === 3 && guid.test(third)) return { page: "admin-order", id: third };
    if (parts.length === 2 && second === "products") return { page: "admin-products" };
    if (parts.length === 2 && second === "stock") return { page: "admin-stock" };
  }
  return { page: "not-found" };
}

/** Pages that need a signed-in customer; the admin pages also need the Admin role. */
export const needsSignIn = (route: Route) =>
  ["basket", "checkout", "orders", "order"].includes(route.page) || route.page.startsWith("admin");

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo(0, 0);
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** A link that changes the page without reloading it. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}
