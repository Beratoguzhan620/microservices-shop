# Roadmap

An online shop split into services that talk over messages. The point of the project is consistency without a
shared database: an order either ends up paid with its stock taken, or nothing is taken at all, even when a
service is down halfway through or a message arrives twice.

## Decisions

- **Service per responsibility, database per service.** Identity, Catalog, Basket, Inventory, Ordering and Payment
  each own their data; nothing reads another service's tables.
- **RabbitMQ with MassTransit 8** (Apache 2.0). Events and commands are contracts in one shared library.
- **Transactional outbox and idempotent consumers.** A service's change and the messages it sends commit together;
  a message delivered twice changes nothing the second time.
- **Orchestrated saga.** The order process (reserve stock → take payment → confirm, with compensations and
  timeouts) is a MassTransit state machine in Ordering, so the whole flow lives in one place.
- **YARP gateway.** The single entry point: token check, routing, rate limits, correlation ids, one API reference.
- **RS256 tokens.** Identity signs with a private key; the gateway and services only hold the public key.
- **React storefront and admin.**

## Stages

| # | Stage | Contents |
|---|---|---|
| 1 | Skeleton | Shared service defaults, Identity (accounts, RS256 tokens, admin from configuration), Catalog (products, categories, search, admin with optimistic concurrency), YARP gateway, Compose, CI |
| 2 | Basket, stock and orders | Basket in Redis, Inventory with reservations, Ordering checkout with a price snapshot; RabbitMQ, outbox, idempotent consumers |
| 3 | Saga and payment | Order state machine, Payment service with a fake provider, compensations, timeouts, retries and dead letters, tracing across services |
| 4 | Web | React storefront (catalog, basket, checkout, order tracking) and admin (products, stock, orders); Playwright |
| 5 | Resilience and docs | Concurrent checkouts on scarce stock without overselling, a service stopped mid-flow, README in English and Turkish, screenshots and GIF |

## Later

- Real payments through a provider's sandbox.
- Order emails through the notification-service.
- Kubernetes manifests.
