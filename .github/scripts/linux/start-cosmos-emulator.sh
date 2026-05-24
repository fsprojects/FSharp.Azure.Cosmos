#!/usr/bin/env bash
set -euo pipefail

docker run -d --name cosmosdb \
  -p 8081:8081 \
  -p 8080:8080 \
  -p 1234:1234 \
  -e PROTOCOL=https \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
