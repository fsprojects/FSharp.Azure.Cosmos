$emulatorPath = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
Start-Process -FilePath $emulatorPath -ArgumentList '/NoUI /NoExplorer /AllowNetworkAccess /EnablePreview' -PassThru | Out-Null

$maxAttempts = 60
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
	try {
		# An unauthenticated request to the emulator root returns 401 once it is up.
		# PowerShell 7 throws for 4xx responses unless -SkipHttpErrorCheck is set.
		$response = Invoke-WebRequest -Uri 'https://127.0.0.1:8081/' -SkipCertificateCheck -SkipHttpErrorCheck -Method Get -TimeoutSec 5
		if ($response.StatusCode -in 200, 401) {
			Write-Host "Cosmos DB Emulator is ready on Windows (status: $($response.StatusCode))."
			exit 0
		}

		Write-Host "Cosmos DB Emulator returned status $($response.StatusCode) (attempt $attempt/$maxAttempts)."
	}
	catch {
		Write-Host "Cosmos DB Emulator is not ready yet (attempt $attempt/$maxAttempts)."
	}

	Start-Sleep -Seconds 5
}

# Warn instead of failing: the Windows job runs the build-only target, so it does not need the emulator.
Write-Warning 'Cosmos DB Emulator failed to become ready on Windows. Continuing because Windows job runs build-only target.'
