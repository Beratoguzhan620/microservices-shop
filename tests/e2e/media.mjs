// Screenshots and walkthrough frames for the README, from the real stack with realistic orders.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d && node media.mjs <output-dir>
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const JAEGER = process.env.JAEGER_URL ?? "http://localhost:16686";
const OUT = (process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname).replace(/\/?$/, "/");
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-1";
const adminAccount = { email: process.env.ADMIN_EMAIL ?? "admin@shop.local", password: process.env.ADMIN_PASSWORD ?? "admin-password-change-me" };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function call(path, token, body, method = body ? "POST" : "GET") {
  const response = await fetch(BASE + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 || response.status === 202 && !response.headers.get("content-type") ? null : response.json();
}

async function settled(token, id, statuses) {
  for (let i = 0; i < 120; i++) {
    const response = await fetch(`${BASE}/api/ordering/orders/${id}`, { headers: { Authorization: `Bearer ${token}` } });
    const order = response.ok ? await response.json() : null; // 404 until Ordering has the checkout
    if (order && statuses.includes(order.status)) return order;
    await sleep(500);
  }
  throw new Error(`order ${id} did not reach ${statuses.join("/")}`);
}

const products = (await call("/api/catalog/products?pageSize=50")).items;
const bySku = (sku) => products.find((p) => p.sku === sku);
const admin = (await call("/api/identity/login", null, adminAccount)).accessToken;

/** A customer places an order through the API, the same calls the storefront makes. */
async function order(token, name, lines, card = "4242 4242 4242 4242") {
  for (const [sku, quantity] of lines) await call(`/api/basket/items/${bySku(sku).id}`, token, { quantity }, "PUT");
  const basket = await call("/api/basket", token);
  const { token: paymentToken } = await call("/api/payment/cards", token, { number: card, expiryMonth: 11, expiryYear: 2029, cvc: "123" });
  const address = { fullName: name, line1: "Cumhuriyet Mah. 19 Mayıs Bulvarı No: 12", city: "Samsun", postalCode: "55030", phone: "0555 222 33 44" };
  const { orderId } = await call("/api/basket/checkout", token, { address, expectedTotal: basket.total, paymentToken });
  return orderId;
}

const people = {};
for (const [key, name] of Object.entries({ ayse: "Ayşe Yılmaz", mehmet: "Mehmet Demir", zeynep: "Zeynep Kaya", can: "Can Öztürk", elif: "Elif Şahin" })) {
  const email = `${key}.${run}@example.com`;
  const session = await call("/api/identity/register", null, { email, displayName: name, password });
  people[key] = { name, email, token: session.accessToken };
}

// A morning of orders: shipped, paid, declined, cancelled with a refund.
const shipped = await order(people.mehmet.token, people.mehmet.name, [["KHV-003", 1], ["DML-004", 2]]);
await settled(people.mehmet.token, shipped, ["Confirmed"]);
await call(`/api/ordering/admin/orders/${shipped}/ship`, admin, { trackingCode: "YK-48213907" });
const zeynep = await order(people.zeynep.token, people.zeynep.name, [["CAY-001", 2], ["CAY-004", 1]]);
await settled(people.zeynep.token, zeynep, ["Confirmed"]);
await order(people.can.token, people.can.name, [["EKP-003", 1]], "4000 0000 0000 0002");
const elif = await order(people.elif.token, people.elif.name, [["KHV-001", 1], ["EKP-001", 1]]);
await settled(people.elif.token, elif, ["Confirmed"]);
await call(`/api/ordering/orders/${elif}/cancel`, people.elif.token, { reason: "Yanlış ürünü seçtim." });
const ayseEarlier = await order(people.ayse.token, people.ayse.name, [["KHV-002", 2]]);
await settled(people.ayse.token, ayseEarlier, ["Confirmed"]);
await call(`/api/ordering/admin/orders/${ayseEarlier}/ship`, admin, { trackingCode: "YK-48214552" });
await sleep(2000);

const browser = await chromium.launch();
async function signedIn(account, viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", deviceScaleFactor: 1 });
  const page = await context.newPage();
  await page.goto(`${BASE}/signin`);
  await page.getByLabel("Email").fill(account.email);
  await page.getByLabel("Password").fill(account.password ?? password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/signin"));
  return page;
}

const shot = async (page, name) => {
  await page.waitForLoadState("networkidle");
  await sleep(400);
  await page.screenshot({ path: `${OUT}${name}.png` });
  console.log(`saved ${name}.png`);
};

// The storefront, signed in as Ayşe.
const ayse = await signedIn(people.ayse);
await ayse.goto(BASE);
await ayse.locator('[data-product="Etiyopya Yirgacheffe 250 g"]').waitFor();
await shot(ayse, "catalog");
await ayse.goto(`${BASE}/products/${bySku("KHV-004").slug}`);
await ayse.getByRole("heading", { name: "Kenya AA 250 g" }).waitFor();
await shot(ayse, "product");
await ayse.getByRole("button", { name: "One more" }).click();
await ayse.getByRole("button", { name: "Add to basket" }).click();
await ayse.getByRole("status").filter({ hasText: "In your basket" }).waitFor();
await call(`/api/basket/items/${bySku("DML-001").id}`, people.ayse.token, { quantity: 1 }, "PUT");
await ayse.goto(`${BASE}/basket`);
await ayse.getByRole("button", { name: "One more Kenya AA 250 g" }).waitFor();
await shot(ayse, "basket");

await ayse.goto(`${BASE}/checkout`);
await ayse.getByLabel("Full name").fill(people.ayse.name);
await ayse.getByLabel("Address").fill("Atatürk Cad. No: 24/5");
await ayse.getByLabel("City").fill("Samsun");
await ayse.getByLabel("Postal code").fill("55060");
await ayse.getByLabel("Phone").fill("0555 123 45 67");
await ayse.getByLabel("Card number").fill("4242 4242 4242 4242");
await ayse.getByLabel("Expiry").fill("12/30");
await ayse.getByLabel("CVC").fill("123");
await shot(ayse, "checkout");
await ayse.getByRole("button", { name: /^Pay / }).click();
await ayse.waitForURL(/\/orders\/[0-9a-f-]{36}$/);
await ayse.locator(".badge", { hasText: "Paid" }).waitFor({ timeout: 30000 });
await shot(ayse, "order-paid");
await ayse.goto(`${BASE}/orders/${ayseEarlier}`);
await ayse.locator(".badge", { hasText: "Shipped" }).waitFor();
await shot(ayse, "order-shipped");
await ayse.goto(`${BASE}/orders`);
await shot(ayse, "orders");

// What Can sees after a declined card.
{
  const can = await signedIn(people.can);
  await can.goto(`${BASE}/orders`);
  await can.locator(".line").first().click();
  await can.getByText("The card was declined.").waitFor({ timeout: 30000 });
  await shot(can, "order-declined");
  await can.context().close();
}

// The admin.
const shop = await signedIn(adminAccount);
await shop.goto(`${BASE}/admin/orders`);
await shop.getByRole("row").filter({ hasText: people.zeynep.name }).first().waitFor();
await shot(shop, "admin-orders");
await shop.getByRole("row").filter({ hasText: people.zeynep.name }).first().click();
await shop.getByLabel("Tracking code").waitFor();
await shot(shop, "admin-order");
await shop.goto(`${BASE}/admin/stock`);
await shop.getByRole("row").nth(3).waitFor();
await shot(shop, "admin-stock");
await shop.goto(`${BASE}/admin/products`);
await shop.getByRole("button", { name: "Edit Kenya AA 250 g" }).waitFor();
await shot(shop, "admin-products");

// Phone.
{
  const phone = await signedIn(people.ayse, { width: 390, height: 844 });
  await phone.goto(BASE);
  await phone.locator("[data-product]").first().waitFor();
  await shot(phone, "phone-catalog");
  await phone.goto(`${BASE}/orders/${ayseEarlier}`);
  await phone.locator(".badge", { hasText: "Shipped" }).waitFor();
  await shot(phone, "phone-order");
  await phone.context().close();
}

// API reference through the gateway, and one checkout as a trace across the services.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/scalar`);
  await page.waitForLoadState("networkidle");
  await sleep(1500);
  await shot(page, "api-reference");
  try {
    // The checkout trace that touches the most services: basket, ordering, inventory and payment.
    const found = await (await fetch(`${JAEGER}/api/traces?service=basket&limit=100&lookback=1h`)).json();
    const services = (trace) => new Set(Object.values(trace.processes).map((p) => p.serviceName)).size;
    const best = found.data.sort((a, b) => services(b) - services(a) || b.spans.length - a.spans.length)[0];
    if (!best) throw new Error("no checkout trace in Jaeger");
    const href = `/trace/${best.traceID}`;
    await page.goto(new URL(href, JAEGER).toString());
    await page.waitForLoadState("networkidle");
    await sleep(1500);
    await shot(page, "trace");
  } catch (error) {
    console.log(`no trace screenshot: ${error.message.split("\n")[0]}`);
  }
  await context.close();
}

// Walkthrough frames: browse, add, pay, watch the order go through, and the admin ships it.
const film = await signedIn(people.zeynep, { width: 1100, height: 720 });
let frame = 0;
async function frames(count = 1, gap = 0) {
  for (let i = 0; i < count; i++) {
    await film.screenshot({ path: `${OUT}frames/${String(frame++).padStart(3, "0")}.png` });
    if (gap) await sleep(gap);
  }
}
await film.goto(BASE);
await film.locator("[data-product]").first().waitFor();
await sleep(500);
await frames(4);
await film.getByPlaceholder("Search coffee, tea, gear").pressSequentially("kenya", { delay: 80 });
await film.locator('[data-product="Kenya AA 250 g"]').waitFor();
await sleep(400);
await frames(3);
await film.locator('[data-product="Kenya AA 250 g"]').click();
await film.getByRole("heading", { name: "Kenya AA 250 g" }).waitFor();
await sleep(400);
await frames(3);
await film.getByRole("button", { name: "Add to basket" }).click();
await film.getByRole("status").filter({ hasText: "In your basket" }).waitFor();
await frames(3);
await film.goto(`${BASE}/checkout`);
await film.getByLabel("Full name").waitFor();
await frames(2);
for (const [label, value] of [["Full name", people.zeynep.name], ["Address", "Bağdat Cad. No: 210/4"], ["City", "Samsun"], ["Postal code", "55200"], ["Phone", "0555 987 65 43"]]) {
  await film.getByLabel(label).fill(value);
}
await frames(2);
await film.getByLabel("Card number").pressSequentially("4242 4242 4242 4242", { delay: 25 });
await film.getByLabel("Expiry").fill("12/30");
await film.getByLabel("CVC").fill("123");
await frames(3);
await film.getByRole("button", { name: /^Pay / }).click();
await film.waitForURL(/\/orders\/[0-9a-f-]{36}$/);
const filmOrder = film.url().split("/").pop();
for (let i = 0; i < 24; i++) {
  await frames(1);
  if (await film.locator(".badge", { hasText: "Paid" }).count()) break;
  await sleep(250);
}
await frames(4);
await call(`/api/ordering/admin/orders/${filmOrder}/ship`, admin, { trackingCode: "YK-48219031" });
await sleep(1500);
await film.reload();
await film.locator(".badge", { hasText: "Shipped" }).waitFor({ timeout: 30000 });
await sleep(300);
await frames(6);
console.log(`saved ${frame} frames`);

await browser.close();
