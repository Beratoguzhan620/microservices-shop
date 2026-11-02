import { LogOut, Package, ShoppingBag, User as UserIcon, Wrench } from "lucide-react";
import { useShop } from "../context";
import { linkProps, navigate, type Route } from "../router";

export function Header({ route }: { route: Route }) {
  const { session, setSession, basket } = useShop();
  const admin = session?.user.role === "Admin";
  const count = basket?.count ?? 0;

  return (
    <header className="header">
      <div className="header-inner">
        <a className="brand" {...linkProps("/")}>
          <span className="brand-mark" aria-hidden="true">D</span>
          Demlik
        </a>
        <nav className="header-nav" aria-label="Main">
          <a className={`nav-link${route.page === "catalog" || route.page === "product" ? " active" : ""}`} {...linkProps("/")}>Shop</a>
          {session && <a className={`nav-link${route.page === "orders" || route.page === "order" ? " active" : ""}`} {...linkProps("/orders")}><Package size={16} />Orders</a>}
          {admin && <a className={`nav-link${route.page.startsWith("admin") ? " active" : ""}`} {...linkProps("/admin/orders")}><Wrench size={16} />Admin</a>}
        </nav>
        <div className="header-actions">
          <a className="basket-button" {...linkProps(session ? "/basket" : "/signin?next=/basket")} aria-label={`Basket, ${count} ${count === 1 ? "item" : "items"}`}>
            <ShoppingBag size={19} />
            {count > 0 && <span className="count">{count}</span>}
          </a>
          {session ? (
            <>
              <span className="who hide-sm"><UserIcon size={15} />{session.user.displayName}</span>
              <button className="icon-button" aria-label="Sign out" title="Sign out" onClick={() => { setSession(null); navigate("/"); }}>
                <LogOut size={17} />
              </button>
            </>
          ) : (
            <a className="button small" {...linkProps("/signin")}>Sign in</a>
          )}
        </div>
      </div>
    </header>
  );
}
