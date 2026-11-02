#!/bin/sh
# Makes the RSA key pair for access tokens once. Identity gets the private key; everyone else only the public one.
set -eu
if [ ! -f /private/private.pem ]; then
  apk add --no-cache openssl > /dev/null
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out /private/private.pem
  openssl pkey -in /private/private.pem -pubout -out /public/public.pem
  chmod 644 /private/private.pem /public/public.pem
  echo "Created a new token signing key pair."
fi
