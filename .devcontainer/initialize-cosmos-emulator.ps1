#!/usr/bin/env pwsh
# Host-side devcontainer initializeCommand.
#
# On GitHub Actions, start the Cosmos DB Emulator container on the runner and wait until it is ready,
# so the dev container (which shares the host network) can run the integration tests against
# 127.0.0.1:8081. Local developers manage their own emulator, so outside GitHub Actions this is a no-op.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:GITHUB_ACTIONS -ne 'true') {
	exit 0
}

$containerName = 'cosmosdb'

# The dev container CLI can run initializeCommand more than once, so only create the container once.
$existing = docker ps --all --filter "name=^$containerName$" --format '{{.Names}}'
if ($existing -contains $containerName) {
	Write-Host 'Cosmos DB Emulator container already exists.'
	docker start $containerName | Out-Null
}
else {
	# Same settings as .github/scripts/linux/start-cosmos-emulator.sh, which the main Linux CI job uses.
	docker run -d --name $containerName `
		-p 8081:8081 -p 8080:8080 -p 1234:1234 `
		-e PROTOCOL=https `
		mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview | Out-Null
}

if ($LASTEXITCODE -ne 0) {
	throw "docker exited with code $LASTEXITCODE."
}

$maxAttempts = 120
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
	try {
		$response = Invoke-WebRequest -Uri 'http://127.0.0.1:8080/ready' -SkipHttpErrorCheck -TimeoutSec 5
		if ($response.StatusCode -eq 200) {
			Write-Host 'Cosmos DB Emulator is ready.'
			exit 0
		}
	}
	catch {
		# The readiness endpoint is not listening yet.
	}

	Write-Host "Cosmos DB Emulator is not ready yet (attempt $attempt/$maxAttempts)."
	Start-Sleep -Seconds 5
}

docker logs --tail 50 $containerName
throw 'Cosmos DB Emulator failed to become ready in time.'
