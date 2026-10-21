#!/usr/bin/env bash
# Polls the bots' logs for SmokeDriver's "OK:" line (tests/MinimalSample/Runtime/SmokeDriver.cs); fails fast on a
# "FAIL:" line and otherwise fails after the timeout. Run from smoke/, after `docker compose up -d`.
set -euo pipefail
TIMEOUT=${1:-180}
DEADLINE=$((SECONDS + TIMEOUT))

while (( SECONDS < DEADLINE )); do
  LOGS=$(docker compose logs --no-color bot1 bot2 2>/dev/null || true)
  if echo "$LOGS" | grep -q "SmokeDriver:bot1\] OK:" && echo "$LOGS" | grep -q "SmokeDriver:bot2\] OK:"; then
    echo "Both bots exchanged RPCs."
    exit 0
  fi
  if echo "$LOGS" | grep -q "FAIL:"; then
    echo "A bot reported failure:"
    echo "$LOGS" | grep "FAIL:"
    exit 1
  fi
  sleep 3
done

echo "Timed out after ${TIMEOUT}s waiting for both bots."
docker compose logs --no-color
exit 1
