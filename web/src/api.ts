import type {
  Address, Availability, Basket, CardToken, Category, Order, OrderPage, OrderStatus, OrderSummary, Product, ProductPage, ProductSort, Session, StockItem, User,
} from "./types";

/** An API error with the stable code from the problem response, such as "out_of_stock" or "basket_changed". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
    public readonly body: Record<string, unknown> | null = null,
  ) {
    super(message);
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch {
    throw new ApiError("The shop cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields, problem);
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });

export interface ProductQuery { q?: string; category?: string; sort?: ProductSort; page?: number; pageSize?: number; includeArchived?: boolean }

export function productQueryString(query: ProductQuery): string {
  const params = new URLSearchParams();
  if (query.q?.trim()) params.set("q", query.q.trim());
  if (query.category) params.set("category", query.category);
  if (query.sort && query.sort !== "Newest") params.set("sort", query.sort);
  if (query.page && query.page > 1) params.set("page", String(query.page));
  if (query.pageSize) params.set("pageSize", String(query.pageSize));
  if (query.includeArchived) params.set("includeArchived", "true");
  const text = params.toString();
  return text ? `?${text}` : "";
}

const ids = (list: string[]) => list.map((id) => `ids=${encodeURIComponent(id)}`).join("&");

export const api = {
  register: (email: string, displayName: string, password: string) =>
    request<Session>("/api/identity/register", post({ email, displayName, password })),
  login: (email: string, password: string) => request<Session>("/api/identity/login", post({ email, password })),
  me: () => request<User>("/api/identity/me"),

  categories: () => request<Category[]>("/api/catalog/categories"),
  products: (query: ProductQuery) => request<ProductPage>(`/api/catalog/products${productQueryString(query)}`),
  product: (slug: string) => request<Product>(`/api/catalog/products/${encodeURIComponent(slug)}`),
  availability: (productIds: string[]) =>
    productIds.length === 0 ? Promise.resolve([] as Availability[]) : request<Availability[]>(`/api/inventory/availability?${ids(productIds)}`),

  basket: () => request<Basket>("/api/basket"),
  setQuantity: (productId: string, quantity: number) => request<Basket>(`/api/basket/items/${productId}`, { method: "PUT", body: json({ quantity }) }),
  addCard: (number: string, expiryMonth: number, expiryYear: number, cvc: string) =>
    request<CardToken>("/api/payment/cards", post({ number, expiryMonth, expiryYear, cvc })),
  checkout: (address: Address, expectedTotal: number, paymentToken: string) =>
    request<{ orderId: string }>("/api/basket/checkout", post({ address, expectedTotal, paymentToken })),

  orders: () => request<OrderSummary[]>("/api/ordering/orders"),
  order: (id: string) => request<Order>(`/api/ordering/orders/${id}`),
  cancel: (id: string, reason?: string) => request<Order>(`/api/ordering/orders/${id}/cancel`, post({ reason })),

  adminOrders: (status?: OrderStatus, page = 1) =>
    request<OrderPage>(`/api/ordering/admin/orders?pageSize=25&page=${page}${status ? `&status=${status}` : ""}`),
  ship: (id: string, trackingCode: string) => request<Order>(`/api/ordering/admin/orders/${id}/ship`, post({ trackingCode })),
  createProduct: (body: { sku: string; name: string; description: string; price: number; categoryId: string }) =>
    request<Product>("/api/catalog/products", post(body)),
  updateProduct: (id: string, body: { name: string; description: string; price: number; categoryId: string; version: number }) =>
    request<Product>(`/api/catalog/products/${id}`, { method: "PUT", body: json(body) }),
  archive: (id: string) => request<Product>(`/api/catalog/products/${id}/archive`, post()),
  restore: (id: string) => request<Product>(`/api/catalog/products/${id}/restore`, post()),
  stock: (q?: string) => request<StockItem[]>(`/api/inventory/stock${q?.trim() ? `?q=${encodeURIComponent(q.trim())}` : ""}`),
  count: (productId: string, onHand: number, version: number) =>
    request<StockItem>(`/api/inventory/stock/${productId}`, { method: "PUT", body: json({ onHand, version }) }),
};
