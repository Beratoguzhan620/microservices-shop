#!/usr/bin/env bash
# Resilience against the running Compose stack, started with the gateway limits raised:
#   AUTH_ATTEMPTS_PER_MINUTE=100000 REQUESTS_PER_MINUTE=100000 docker compose up --build --detach --wait
#   1. scarce stock: many customers check out the last units at the same moment;
#   2. Ordering is stopped while orders are placed, then started: every order still completes;
#   3. Payment is stopped while orders wait to be charged, then started: every order still completes.
set -euo pipefail
cd "$(dirname "$0")"
state=$(mktemp -d)
compose() { docker compose -f ../../docker-compose.yml "$@"; }

echo "== Scarce stock"
node shop.mjs oversell 10 60

echo
echo "== Ordering stopped while 15 orders are placed"
node shop.mjs prepare "$state/ordering.json" 15
compose stop ordering > /dev/null
node shop.mjs place "$state/ordering.json"
echo "Ordering is down; the checkouts wait in RabbitMQ. Starting it again in 10 seconds."
sleep 10
compose start ordering > /dev/null
node shop.mjs settle "$state/ordering.json" Confirmed

echo
echo "== Payment stopped while 15 orders wait to be charged"
node shop.mjs prepare "$state/payment.json" 15
compose stop payment > /dev/null
node shop.mjs place "$state/payment.json"
echo "Payment is down; the orders hold their stock and wait. Starting it again in 15 seconds."
sleep 15
compose start payment > /dev/null
node shop.mjs settle "$state/payment.json" Confirmed

echo
echo "resilience held"
