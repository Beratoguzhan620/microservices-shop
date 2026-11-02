// A customer and the shop's admin, in separate browsers, against the whole Compose stack.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d && cd tests/e2e && npm install && npx playwright install chromium && node shop.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const customer = { name: "Ayşe Yılmaz", email: `ayse.${run}@example.com`, password: "e2e-test-password" };
const adminAccount = { email: process.env.ADMIN_EMAIL ?? "admin@shop.local", password: process.env.ADMIN_PASSWORD ?? "admin-password-change-me" };

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
  }
}

async function open(viewport = { width: 1280, height: 860 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB" });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource/.test(message.text())) errors.push(message.text());
  });
  return page;
}

const shopper = await open();
const admin = await open();

async function addToBasket(page, productName, quantity = 1) {
  await page.goto(BASE);
  await page.getByPlaceholder("Search coffee, tea, gear").fill(productName);
  await page.locator(`[data-product="${productName}"]`).click();
  await page.getByRole("heading", { name: productName }).waitFor();
  for (let i = 1; i < quantity; i++) await page.getByRole("button", { name: "One more" }).click();
  await page.getByRole("button", { name: "Add to basket" }).click();
  await page.getByRole("status").filter({ hasText: "In your basket" }).waitFor();
}

async function payWith(page, card) {
  await page.goto(`${BASE}/checkout`);
  await page.getByLabel("Full name").fill(customer.name);
  await page.getByLabel("Address").fill("Atatürk Cad. No: 24/5");
  await page.getByLabel("City").fill("Samsun");
  await page.getByLabel("Postal code").fill("55060");
  await page.getByLabel("Phone").fill("0555 123 45 67");
  await page.getByLabel("Card number").fill(card);
  await page.getByLabel("Expiry").fill("12/30");
  await page.getByLabel("CVC").fill("123");
  await page.getByRole("button", { name: /^Pay / }).click();
  await page.waitForURL(/\/orders\/[0-9a-f-]{36}$/);
  return page.url().split("/").pop();
}

let paidOrder = "";

await step("visitors browse by category and search without an account", async () => {
  await shopper.goto(BASE);
  await shopper.getByRole("heading", { name: "Coffee, tea and the tools to brew them" }).waitFor();
  await shopper.getByRole("tab", { name: /Çay/ }).click();
  await shopper.getByRole("heading", { name: "Çay" }).waitFor();
  await shopper.locator('[data-product="Kenya AA 250 g"]').waitFor({ state: "detached", timeout: 10000 })
    .catch(() => { throw new Error("coffee still shows under tea"); });
  await shopper.locator('[data-product="Earl Grey 100 g"]').waitFor();
  await shopper.getByRole("tab", { name: "All" }).click();
  await shopper.getByPlaceholder("Search coffee, tea, gear").fill("kenya");
  await shopper.locator('[data-product="Kenya AA 250 g"]').waitFor();
  await shopper.getByText("In stock").first().waitFor();
});

await step("adding to the basket asks for an account and comes back to the product", async () => {
  await shopper.locator('[data-product="Kenya AA 250 g"]').click();
  await shopper.getByRole("button", { name: "Add to basket" }).click();
  await shopper.getByRole("heading", { name: "Sign in" }).waitFor();
  await shopper.getByRole("link", { name: "Create an account" }).click();
  await shopper.getByLabel("Email").fill(customer.email);
  await shopper.getByLabel("Your name").fill(customer.name);
  await shopper.getByLabel("Password").fill(customer.password);
  await shopper.getByRole("button", { name: "Create account" }).click();
  await shopper.getByRole("heading", { name: "Kenya AA 250 g" }).waitFor();
  await shopper.getByRole("button", { name: "One more" }).click();
  await shopper.getByRole("button", { name: "Add to basket" }).click();
  await shopper.getByRole("link", { name: "Basket, 2 items" }).waitFor();
});

await step("the basket changes quantities and totals", async () => {
  await shopper.getByRole("link", { name: "Basket, 2 items" }).click();
  await shopper.getByRole("heading", { name: "Basket" }).waitFor();
  await shopper.getByRole("button", { name: "One more Kenya AA 250 g" }).click();
  await shopper.getByText("₺1.287,00").first().waitFor();
  await shopper.getByRole("button", { name: "One less Kenya AA 250 g" }).click();
  await shopper.getByText("₺858,00").first().waitFor();
});

await step("paying with a good card holds the stock, takes the payment and confirms", async () => {
  paidOrder = await payWith(shopper, "4242 4242 4242 4242");
  await shopper.locator(".badge", { hasText: "Paid" }).waitFor({ timeout: 30000 });
  await shopper.getByText("Paid with the card ending 4242.").waitFor();
  await shopper.getByRole("link", { name: "Basket, 0 items" }).waitFor();
});

await step("a declined card places nothing and says why", async () => {
  await addToBasket(shopper, "Earl Grey 100 g");
  await payWith(shopper, "4000 0000 0000 0002");
  await shopper.getByText("The order could not be placed").waitFor({ timeout: 30000 });
  await shopper.getByText("The card was declined.").waitFor();
  await shopper.getByText("Nothing was charged").waitFor();
});

await step("the admin ships the paid order with a tracking code", async () => {
  await admin.goto(`${BASE}/signin`);
  await admin.getByLabel("Email").fill(adminAccount.email);
  await admin.getByLabel("Password").fill(adminAccount.password);
  await admin.getByRole("button", { name: "Sign in" }).click();
  await admin.getByRole("link", { name: "Admin" }).click();
  await admin.getByRole("row").filter({ hasText: customer.name }).first().click();
  await admin.getByLabel("Tracking code").fill(`YK-${run}`.toUpperCase());
  await admin.getByRole("button", { name: "Mark as shipped" }).click();
  await admin.locator(".badge", { hasText: "Shipped" }).first().waitFor({ timeout: 30000 });
});

await step("the customer sees it shipped with the tracking code", async () => {
  await shopper.goto(`${BASE}/orders/${paidOrder}`);
  await shopper.locator(".badge", { hasText: "Shipped" }).waitFor();
  await shopper.getByText(`YK-${run}`.toUpperCase()).waitFor();
  await shopper.goto(`${BASE}/orders`);
  await shopper.locator(".line", { hasText: "Shipped" }).waitFor();
});

await step("cancelling a paid order refunds it", async () => {
  await addToBasket(shopper, "Sencha Yeşil Çay 100 g");
  await payWith(shopper, "4242 4242 4242 4242");
  await shopper.locator(".badge", { hasText: "Paid" }).waitFor({ timeout: 30000 });
  await shopper.getByRole("button", { name: "Cancel the order" }).click();
  await shopper.locator(".badge", { hasText: "Cancelled" }).waitFor({ timeout: 30000 });
  await shopper.getByText("The payment was refunded").waitFor({ timeout: 30000 });
});

await step("a price change before paying is shown and accepted, never charged silently", async () => {
  await addToBasket(shopper, "French Press 1 L");
  await admin.goto(`${BASE}/admin/products`);
  await admin.getByPlaceholder("Name or SKU").fill("French Press");
  await admin.getByRole("button", { name: "Edit French Press 1 L" }).click();
  await admin.getByLabel("Price (₺)").fill("949");
  await admin.getByRole("button", { name: "Save" }).click();
  await admin.getByRole("cell", { name: "₺949,00" }).waitFor();

  await shopper.goto(`${BASE}/checkout`);
  await shopper.getByText("₺899,00").first().waitFor();
  await shopper.getByLabel("Full name").fill(customer.name);
  await shopper.getByLabel("Address").fill("Atatürk Cad. No: 24/5");
  await shopper.getByLabel("City").fill("Samsun");
  await shopper.getByLabel("Postal code").fill("55060");
  await shopper.getByLabel("Phone").fill("0555 123 45 67");
  await shopper.getByLabel("Card number").fill("4242 4242 4242 4242");
  await shopper.getByLabel("Expiry").fill("12/30");
  await shopper.getByLabel("CVC").fill("123");
  await shopper.getByRole("button", { name: "Pay ₺899,00" }).click();
  await shopper.getByText("Prices changed since you added these.").waitFor();
  await shopper.getByRole("button", { name: "Pay ₺949,00" }).click();
  await shopper.locator(".badge", { hasText: "Paid" }).waitFor({ timeout: 30000 });
});

await step("the admin sees stock that is held, and counts it", async () => {
  await admin.goto(`${BASE}/admin/stock`);
  await admin.getByPlaceholder("Name or SKU").fill("French Press");
  const row = admin.getByRole("row").filter({ hasText: "French Press 1 L" });
  await row.getByRole("cell", { name: "1", exact: true }).first().waitFor();
  await admin.getByLabel("Stock of French Press 1 L").fill("40");
  await admin.getByRole("button", { name: "Save stock of French Press 1 L" }).click();
  await row.getByRole("cell", { name: "39", exact: true }).waitFor();
});

await step("customers cannot open the admin pages", async () => {
  await shopper.goto(`${BASE}/admin/orders`);
  await shopper.getByText("This page is for the shop's staff").waitFor();
});

await step("the shop works on a phone-sized screen", async () => {
  await shopper.setViewportSize({ width: 390, height: 844 });
  for (const path of ["/", "/products/kenya-aa-250-g", `/orders/${paidOrder}`, "/checkout"]) {
    await shopper.goto(BASE + path);
    await shopper.waitForLoadState("networkidle");
    const overflow = await shopper.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (overflow > 1) throw new Error(`${path} scrolls sideways by ${overflow}px`);
  }
});

await step("no errors in the browser console", async () => {
  if (errors.length) throw new Error(errors.join(" | "));
});

await shopper.screenshot({ path: `${OUT}last.png` });
await browser.close();
if (failures) {
  console.log(`\n${failures} step(s) failed`);
  process.exit(1);
}
console.log("\nall steps passed");
