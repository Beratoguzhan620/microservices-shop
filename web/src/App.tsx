import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { ShopCtx } from "./context";
import { navigate, needsSignIn, useRoute } from "./router";
import { loadSession, saveSession } from "./session";
import type { Basket, Session } from "./types";
import { Header } from "./components/Header";
import { Empty } from "./components/States";
import { Catalog } from "./pages/Catalog";
import { ProductPage } from "./pages/Product";
import { BasketPage } from "./pages/Basket";
import { Checkout } from "./pages/Checkout";
import { Orders, OrderPage } from "./pages/Orders";
import { SignIn } from "./pages/SignIn";
import { AdminLayout } from "./pages/admin/AdminLayout";
import { AdminOrders, AdminOrder } from "./pages/admin/AdminOrders";
import { AdminProducts } from "./pages/admin/AdminProducts";
import { AdminStock } from "./pages/admin/AdminStock";

export function App() {
  const route = useRoute();
  const [session, setSessionState] = useState<Session | null>(loadSession);
  const [basket, setBasket] = useState<Basket | null>(null);

  const setSession = useCallback((next: Session | null) => {
    saveSession(next);
    setSessionState(next);
    if (!next) setBasket(null);
  }, []);

  setToken(session?.accessToken ?? null, () => setSession(null));

  const refreshBasket = useCallback(() => {
    if (!session) return;
    api.basket().then(setBasket, () => {});
  }, [session]);

  useEffect(refreshBasket, [refreshBasket]);

  useEffect(() => {
    if (!session && needsSignIn(route)) navigate(`/signin?next=${encodeURIComponent(location.pathname)}`, true);
  }, [session, route]);

  const context = useMemo(() => ({ session, setSession, basket, setBasket, refreshBasket }), [session, setSession, basket, refreshBasket]);
  const admin = session?.user.role === "Admin";

  let page: React.ReactNode;
  switch (route.page) {
    case "catalog":
      page = <Catalog category={route.category} q={route.q} />;
      break;
    case "product":
      page = <ProductPage slug={route.slug} />;
      break;
    case "signin":
    case "register":
      page = <SignIn mode={route.page} next={route.next} />;
      break;
    case "basket":
      page = session && <BasketPage />;
      break;
    case "checkout":
      page = session && <Checkout />;
      break;
    case "orders":
      page = session && <Orders />;
      break;
    case "order":
      page = session && <OrderPage id={route.id} />;
      break;
    case "admin-orders":
    case "admin-order":
    case "admin-products":
    case "admin-stock":
      page = !session ? null : !admin ? (
        <Empty title="This page is for the shop's staff" />
      ) : (
        <AdminLayout route={route}>
          {route.page === "admin-orders" && <AdminOrders />}
          {route.page === "admin-order" && <AdminOrder id={route.id} />}
          {route.page === "admin-products" && <AdminProducts />}
          {route.page === "admin-stock" && <AdminStock />}
        </AdminLayout>
      );
      break;
    default:
      page = <Empty title="This page does not exist"><a href="/" onClick={(e) => { e.preventDefault(); navigate("/"); }}>Back to the shop</a></Empty>;
  }

  return (
    <ShopCtx.Provider value={context}>
      <Header route={route} />
      <main className="main">{page}</main>
      <footer className="footer">Demlik is a demo shop: no real orders, payments or deliveries.</footer>
    </ShopCtx.Provider>
  );
}
