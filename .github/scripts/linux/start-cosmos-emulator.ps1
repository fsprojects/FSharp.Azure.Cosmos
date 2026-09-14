#!/usr/bin/env pwsh
# Starts the Linux (vNext) Azure Cosmos DB Emulator as a Docker container on the host.
#
# Used by the Linux CI job and by the dev container initializeCommand. Safe to run more than once:
# if the container already exists, it is started instead of created again.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$containerName = 'cosmosdb'

$existing = docker ps --all --filter "name=^$containerName$" --format '{{.Names}}'
if ($existing -contains $containerName) {
	Write-Host "Cosmos DB Emulator container '$containerName' already exists; starting it."
	docker start $containerName | Out-Null
}
else {
	docker run -d --name $containerName `
		-p 8081:8081 `
		-p 8080:8080 `
		-p 1234:1234 `
		-e PROTOCOL=https `
		mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
}

if ($LASTEXITCODE -ne 0) {
	throw "docker exited with code $LASTEXITCODE."
}
