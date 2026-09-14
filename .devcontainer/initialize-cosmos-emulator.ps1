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

# The dev container CLI can run initializeCommand more than once; the start script handles an existing container.
& (Join-Path $PSScriptRoot '..' '.github' 'scripts' 'linux' 'start-cosmos-emulator.ps1')

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

docker logs --tail 50 cosmosdb
throw 'Cosmos DB Emulator failed to become ready in time.'
