export type Role = "Customer" | "Admin";
export interface User { id: string; email: string; displayName: string; role: Role }
export interface Session { accessToken: string; expiresAt: string; user: User }

export interface Category { id: string; name: string; slug: string; productCount: number }
export interface CategoryRef { id: string; name: string; slug: string }
export interface Product {
  id: string;
  sku: string;
  name: string;
  slug: string;
  description: string;
  price: number;
  currency: string;
  category: CategoryRef;
  isActive: boolean;
  updatedAt: string;
  version: number;
}
export interface ProductPage { items: Product[]; total: number; page: number; pageSize: number }
export type ProductSort = "Newest" | "PriceAsc" | "PriceDesc" | "Name";
export interface Availability { productId: string; available: number }

export interface BasketLine { productId: string; sku: string; name: string; unitPrice: number; quantity: number; total: number }
export interface Basket { lines: BasketLine[]; count: number; total: number; currency: string }
export interface Address { fullName: string; line1: string; city: string; postalCode: string; phone: string }
export interface CardToken { token: string; brand: string; last4: string; expiresAt: string }

export type OrderStatus = "Submitted" | "AwaitingPayment" | "Confirmed" | "Shipped" | "Rejected" | "Cancelled";
export interface OrderItem { productId: string; sku: string; name: string; unitPrice: number; quantity: number; total: number }
export interface Order {
  id: string;
  status: OrderStatus;
  reason: string | null;
  total: number;
  currency: string;
  items: OrderItem[];
  shippingAddress: Address;
  customerName: string;
  customerEmail: string;
  cardLast4: string | null;
  trackingCode: string | null;
  refunded: boolean;
  canCancel: boolean;
  createdAt: string;
  updatedAt: string;
}
export interface OrderSummary { id: string; status: OrderStatus; total: number; itemCount: number; customerName: string; createdAt: string; updatedAt: string }
export interface OrderPage { items: OrderSummary[]; total: number; page: number; pageSize: number }

export interface StockItem { productId: string; sku: string; name: string; onHand: number; reserved: number; available: number; updatedAt: string; version: number }
