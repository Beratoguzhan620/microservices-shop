import { createContext, useContext } from "react";
import type { Basket, Session } from "./types";

export interface ShopContext {
  session: Session | null;
  setSession: (session: Session | null) => void;
  basket: Basket | null;
  setBasket: (basket: Basket | null) => void;
  refreshBasket: () => void;
}

export const ShopCtx = createContext<ShopContext | null>(null);

export function useShop(): ShopContext {
  const value = useContext(ShopCtx);
  if (!value) throw new Error("useShop outside the app");
  return value;
}
