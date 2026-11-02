import { describe, expect, it } from "vitest";
import { formatCard, isInFlight, money, parseExpiry, progress, shortId } from "./format";
import { needsSignIn, parseRoute } from "./router";
import { productQueryString } from "./api";

describe("format", () => {
  it("writes lira the Turkish way", () => {
    expect(money(1149)).toBe("₺1.149,00");
    expect(money(389.9)).toBe("₺389,90");
  });

  it("groups card numbers and reads expiry dates", () => {
    expect(formatCard("4242a4242 42424242")).toBe("4242 4242 4242 4242");
    expect(parseExpiry("12/30")).toEqual([12, 2030]);
    expect(parseExpiry("1 / 2031")).toEqual([1, 2031]);
    expect(parseExpiry("13/30")).toBeNull();
  });

  it("shows how far an order got", () => {
    expect(progress("AwaitingPayment").reached).toBe(1);
    expect(progress("Shipped").reached).toBe(3);
    expect(progress("Rejected").failed).toBe(true);
    expect(isInFlight("Submitted")).toBe(true);
    expect(isInFlight("Confirmed")).toBe(false);
  });

  it("shortens order ids for people", () => {
    expect(shortId("0195f3c2-8a1b-7c4d-9e2f-3a4b5c6d7e8f")).toBe("5C6D7E8F");
  });
});

describe("routes", () => {
  it("parses shop and admin pages", () => {
    expect(parseRoute("/", "?category=kahve")).toEqual({ page: "catalog", category: "kahve", q: undefined });
    expect(parseRoute("/products/kenya-aa-250-g", "")).toEqual({ page: "product", slug: "kenya-aa-250-g" });
    expect(parseRoute("/orders/0195f3c2-8a1b-7c4d-9e2f-3a4b5c6d7e8f", "")).toEqual({ page: "order", id: "0195f3c2-8a1b-7c4d-9e2f-3a4b5c6d7e8f" });
    expect(parseRoute("/orders/nope", "").page).toBe("not-found");
    expect(parseRoute("/admin", "").page).toBe("admin-orders");
    expect(parseRoute("/signin", "?next=/checkout")).toEqual({ page: "signin", next: "/checkout" });
  });

  it("knows which pages need an account", () => {
    expect(needsSignIn({ page: "checkout" })).toBe(true);
    expect(needsSignIn({ page: "admin-stock" })).toBe(true);
    expect(needsSignIn({ page: "catalog" })).toBe(false);
  });

  it("leaves default query values out", () => {
    expect(productQueryString({})).toBe("");
    expect(productQueryString({ q: " kenya ", sort: "Newest", page: 1 })).toBe("?q=kenya");
    expect(productQueryString({ category: "cay", sort: "PriceAsc", page: 2 })).toBe("?category=cay&sort=PriceAsc&page=2");
  });
});
