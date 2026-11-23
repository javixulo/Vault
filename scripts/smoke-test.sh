#!/usr/bin/env bash
# Smoke test of the Docker image: it starts as a non-root user, passes its startup validation (OMV address and credentials
# secret), answers /health and lists the tools over MCP. It does not exercise the tools, and it does not need an OMV: the
# server starts even when OMV does not answer (it only warns).
#
# Usage: scripts/smoke-test.sh [image]        (default: vault-mcp:latest)
set -euo pipefail

image="${1:-vault-mcp:latest}"
name="vault-smoke-$$"
expected_tools=(system_status list_shared_folders)

secret="$(mktemp)"
cleanup() {
  status=$?
  if [ "$status" -ne 0 ] && docker inspect "$name" >/dev/null 2>&1; then
    echo "--- container logs ---" >&2
    docker logs "$name" 2>&1 | tail -30 >&2 || true
  fi
  docker rm -f "$name" >/dev/null 2>&1 || true
  rm -f "$secret"
  exit "$status"
}
trap cleanup EXIT

fail() { echo "SMOKE FAILED: $*" >&2; exit 1; }

echo "Starting $image ..."
# The OMV account is a secret file under /run/secrets, as the compose file mounts it. It is copied into a container that is
# not started yet instead of bind-mounted: bind mounts of temporary files are not shared with Docker Desktop on macOS.
# The file must be readable by the container's non-root user (1000).
printf '{"username":"smoke","password":"not-a-real-password"}' > "$secret"
chmod 644 "$secret"
# Nothing listens on port 1 of the host: the OMV is unreachable, which the server must tolerate.
docker create --name "$name" --user 1000:1000 -p 127.0.0.1::8080 \
  -e Vault__OmvBaseUrl=http://127.0.0.1:1 "$image" >/dev/null \
  || fail "the container could not be created (does the image exist?)"
docker cp "$secret" "$name:/run/secrets/omv_credentials" >/dev/null
docker start "$name" >/dev/null

port=""
for _ in $(seq 1 60); do
  if [ "$(docker inspect -f '{{.State.Running}}' "$name")" != "true" ]; then
    fail "the container stopped on its own (startup validation failed?)"
  fi
  port="$(docker port "$name" 8080/tcp 2>/dev/null | head -1 | sed 's/.*://')"
  if [ -n "$port" ] && curl -fsS -m 2 "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
    break
  fi
  port=""
  sleep 1
done
[ -n "$port" ] || fail "/health did not answer within 60 seconds"
echo "ok: /health answers"

response="$(curl -fsS -m 10 -X POST "http://127.0.0.1:$port/mcp" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')" || fail "tools/list did not answer"
for tool in "${expected_tools[@]}"; do
  grep -q "\"name\":\"$tool\"" <<<"$response" || fail "the tool $tool is not listed"
done
echo "ok: the ${#expected_tools[@]} tools are listed"

echo "Smoke test passed."
