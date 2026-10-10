# Starts the installed Windows Azure Cosmos DB Emulator through its PowerShell module, as the emulator's documentation
# shows for GitHub Actions, and fails unless the emulator then answers on https://127.0.0.1:8081.
# /AllowNetworkAccess is not passed: it requires /Key or /KeyFile as well, and without one the emulator never became
# ready on the hosted runners. The tests reach the emulator on the local host anyway.
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\PSModules\Microsoft.Azure.CosmosDB.Emulator')

# Waits until the emulator accepts requests. Its first start on a fresh runner initialises the data directory, which
# takes several minutes.
Start-CosmosDbEmulator -NoUI -EnablePreview -Timeout 900

# An unauthenticated request to the emulator root returns 401 once it is up.
# PowerShell 7 throws for 4xx responses unless -SkipHttpErrorCheck is set.
$response = Invoke-WebRequest -Uri 'https://127.0.0.1:8081/' -SkipCertificateCheck -SkipHttpErrorCheck -Method Get -TimeoutSec 30
if ($response.StatusCode -notin 200, 401) {
	throw "Cosmos DB Emulator answered with status $($response.StatusCode) after it started."
}

Write-Host "Cosmos DB Emulator is ready on Windows (status: $($response.StatusCode))."
