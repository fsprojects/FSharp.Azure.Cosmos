#!/usr/bin/env bash
# Host-side devcontainer initializeCommand.
#
# On GitHub Actions, start the Cosmos DB Emulator container on the runner and wait until it is ready,
# so the dev container (which shares the host network) can run the integration tests against
# 127.0.0.1:8081. Local developers manage their own emulator, so outside GitHub Actions this is a no-op.
set -euo pipefail

if [ "${GITHUB_ACTIONS:-}" != "true" ]; then
  exit 0
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"

# The dev container CLI can run initializeCommand more than once, so only create the container once.
if docker ps --all --format '{{.Names}}' | grep --quiet --line-regexp cosmosdb; then
  echo "Cosmos DB Emulator container already exists."
  docker start cosmosdb > /dev/null
else
  bash "$repo_root/.github/scripts/linux/start-cosmos-emulator.sh"
fi

max_attempts=120

for attempt in $(seq 1 "$max_attempts"); do
  if curl --silent --fail --connect-timeout 2 --max-time 5 http://127.0.0.1:8080/ready > /dev/null; then
    echo "Cosmos DB Emulator is ready."
    exit 0
  fi

  echo "Cosmos DB Emulator is not ready yet (attempt $attempt/$max_attempts)."
  sleep 5
done

echo "Cosmos DB Emulator failed to become ready in time."
docker logs --tail 50 cosmosdb || true
exit 1
