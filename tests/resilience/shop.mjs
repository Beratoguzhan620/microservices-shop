// Drives the shop through the gateway like many customers would. Used by run.sh; each command is one phase.
//   node shop.mjs oversell <stock> <customers>   — scarce stock, everyone checks out at once
//   node shop.mjs prepare <file> <customers>     — customers with a basket and a card token, saved to the file
//   node shop.mjs place <file>                   — they all check out; the orders are saved to the file
//   node shop.mjs settle <file> <status>         — wait until every remembered order reaches the status
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const ADMIN = { email: process.env.ADMIN_EMAIL ?? "admin@shop.local", password: process.env.ADMIN_PASSWORD ?? "admin-password-change-me" };
import { readFileSync, writeFileSync } from "node:fs";

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const run = Date.now().toString(36);

async function call(path, { token, body, method = body ? "POST" : "GET" } = {}) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  return { status: response.status, data, ms: performance.now() - started };
}

async function must(path, options) {
  const result = await call(path, options);
  if (result.status >= 300) throw new Error(`${options?.method ?? (options?.body ? "POST" : "GET")} ${path}: ${result.status} ${JSON.stringify(result.data)}`);
  return result.data;
}

async function pool(tasks, limit) {
  const results = new Array(tasks.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, tasks.length) }, async () => {
    while (next < tasks.length) {
      const i = next++;
      results[i] = await tasks[i]();
    }
  }));
  return results;
}

const pct = (values, p) => [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.floor((p / 100) * values.length))];
const summary = (values) => `p50 ${pct(values, 50).toFixed(0)} ms · p95 ${pct(values, 95).toFixed(0)} ms`;
const address = { fullName: "Yük Testi", line1: "Atatürk Cad. No: 1", city: "Samsun", postalCode: "55060", phone: "05550000000" };

async function customer(i) {
  const session = await must("/api/identity/register", { body: { email: `load.${run}.${i}@example.com`, displayName: `Müşteri ${i}`, password: "load-test-password" } });
  return session.accessToken;
}

async function product(name) {
  const page = await must(`/api/catalog/products?q=${encodeURIComponent(name)}`);
  return page.items[0];
}

/** Puts one of the product in the customer's basket and checks out with a good card; returns the order id and timings. */
async function checkout(token, productId) {
  const basket = await must(`/api/basket/items/${productId}`, { token, body: { quantity: 1 }, method: "PUT" });
  const card = await must("/api/payment/cards", { token, body: { number: "4242 4242 4242 4242", expiryMonth: 12, expiryYear: 2030, cvc: "123" } });
  const result = await call("/api/basket/checkout", { token, body: { address, expectedTotal: basket.total, paymentToken: card.token } });
  if (result.status !== 202) throw new Error(`checkout: ${result.status} ${JSON.stringify(result.data)}`);
  return { orderId: result.data.orderId, checkoutMs: result.ms, placedAt: performance.now() };
}

async function settle(orders, done, timeoutSeconds = 120, every = 250) {
  const until = Date.now() + timeoutSeconds * 1000;
  const final = new Map();
  while (final.size < orders.length) {
    if (Date.now() > until) throw new Error(`${orders.length - final.size} order(s) did not settle in ${timeoutSeconds} s`);
    await pool(orders.filter((o) => !final.has(o.orderId)).map((o) => async () => {
      const result = await call(`/api/ordering/orders/${o.orderId}`, { token: o.token });
      if (result.status === 200 && done(result.data.status)) final.set(o.orderId, { ...result.data, settledMs: performance.now() - o.placedAt });
    }), 20);
    if (final.size < orders.length) await sleep(every);
  }
  return [...final.values()];
}

const [command, ...args] = process.argv.slice(2);
const admin = (await must("/api/identity/login", { body: ADMIN })).accessToken;

if (command === "oversell") {
  const [stock = 10, customers = 60] = args.map(Number);
  const target = await product("Kolombiya Huila");
  const item = (await must(`/api/inventory/stock?q=${encodeURIComponent(target.sku)}`, { token: admin }))[0];
  await must(`/api/inventory/stock/${target.id}`, { token: admin, method: "PUT", body: { onHand: item.reserved + stock, version: item.version } });
  const before = (await must(`/api/inventory/stock?q=${encodeURIComponent(target.sku)}`, { token: admin }))[0];

  // One order on its own first, as the baseline for how long the saga takes without a crowd.
  const alone = await customer("alone");
  const single = await checkout(alone, (await product("Kenya AA")).id);
  const [baseline] = await settle([{ token: alone, ...single }], (s) => s === "Confirmed", 60, 50);
  console.log(`one order alone: checkout ${single.checkoutMs.toFixed(0)} ms, checkout to confirmed ${baseline.settledMs.toFixed(0)} ms`);

  const tokens = await pool(Array.from({ length: customers }, (_, i) => () => customer(i)), 20);
  console.log(`${customers} customers, ${before.available} ${target.name} in stock; everyone checks out one at the same moment`);
  const started = performance.now();
  const placed = await pool(tokens.map((token) => async () => ({ token, ...(await checkout(token, target.id)) })), customers);
  const orders = await settle(placed, (s) => s === "Confirmed" || s === "Rejected");
  const seconds = (performance.now() - started) / 1000;

  const confirmed = orders.filter((o) => o.status === "Confirmed");
  const rejected = orders.filter((o) => o.status === "Rejected");
  const after = (await must(`/api/inventory/stock?q=${encodeURIComponent(target.sku)}`, { token: admin }))[0];
  const charged = await pool(rejected.map((o) => async () => (await call(`/api/payment/payments/${o.id}`, { token: admin })).status === 200), 20);

  console.log(`settled in ${seconds.toFixed(1)} s: ${confirmed.length} confirmed, ${rejected.length} rejected`);
  console.log(`checkout ${summary(placed.map((p) => p.checkoutMs))}`);
  console.log(`checkout to confirmed ${summary(confirmed.map((o) => o.settledMs))}`);
  console.log(`stock before: ${before.onHand} on hand, ${before.reserved} held · after: ${after.onHand} on hand, ${after.reserved} held, ${after.available} available`);
  const problems = [];
  if (confirmed.length !== before.available) problems.push(`${confirmed.length} confirmed for ${before.available} in stock`);
  if (after.available !== 0) problems.push(`${after.available} still available`);
  if (after.reserved - before.reserved !== confirmed.length) problems.push(`held went from ${before.reserved} to ${after.reserved}`);
  if (charged.some(Boolean)) problems.push(`${charged.filter(Boolean).length} rejected order(s) were charged`);
  if (problems.length) {
    console.log(`PROBLEMS: ${problems.join("; ")}`);
    process.exit(1);
  }
  console.log("no overselling: exactly as many orders as units went through, and no rejected order was charged");
} else if (command === "prepare") {
  // Customers with a basket and a card token each, ready to check out later (the token comes from Payment).
  const [file, count = 10] = [args[0], Number(args[1] ?? 10)];
  const target = await product("Rize Çayı");
  const item = (await must(`/api/inventory/stock?q=${encodeURIComponent(target.sku)}`, { token: admin }))[0];
  await must(`/api/inventory/stock/${target.id}`, { token: admin, method: "PUT", body: { onHand: item.onHand + count, version: item.version } });
  const ready = await pool(Array.from({ length: count }, (_, i) => async () => {
    const token = await customer(`${file.split("/").pop()}.${i}`);
    const basket = await must(`/api/basket/items/${target.id}`, { token, body: { quantity: 1 }, method: "PUT" });
    const card = await must("/api/payment/cards", { token, body: { number: "4242 4242 4242 4242", expiryMonth: 12, expiryYear: 2030, cvc: "123" } });
    return { token, total: basket.total, paymentToken: card.token };
  }), 10);
  writeFileSync(file, JSON.stringify(ready));
  console.log(`${ready.length} customers ready to check out`);
} else if (command === "place") {
  const file = args[0];
  const ready = JSON.parse(readFileSync(file, "utf8"));
  const placed = await pool(ready.map((r) => async () => {
    const result = await call("/api/basket/checkout", { token: r.token, body: { address, expectedTotal: r.total, paymentToken: r.paymentToken } });
    if (result.status !== 202) throw new Error(`checkout: ${result.status} ${JSON.stringify(result.data)}`);
    return { token: r.token, orderId: result.data.orderId, ms: result.ms };
  }), ready.length);
  writeFileSync(file, JSON.stringify(placed.map(({ token, orderId }) => ({ token, orderId }))));
  console.log(`placed ${placed.length} orders (checkout answered: ${summary(placed.map((p) => p.ms))})`);
} else if (command === "settle") {
  const [file, status] = args;
  const orders = JSON.parse(readFileSync(file, "utf8")).map((o) => ({ ...o, placedAt: performance.now() }));
  const settled = await settle(orders, (s) => s === status || s === "Rejected", 180);
  const wrong = settled.filter((o) => o.status !== status);
  console.log(`${settled.length - wrong.length} of ${settled.length} orders are ${status}`);
  if (wrong.length) {
    console.log(`PROBLEMS: ${wrong.map((o) => `${o.status}: ${o.reason}`).join("; ")}`);
    process.exit(1);
  }
} else {
  console.log("usage: node shop.mjs oversell|prepare|place|settle …");
  process.exit(2);
}
