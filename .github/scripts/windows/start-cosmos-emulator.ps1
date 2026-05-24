$emulatorPath = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
Start-Process -FilePath $emulatorPath -ArgumentList '/NoUI /NoExplorer /AllowNetworkAccess /EnablePreview' -PassThru | Out-Null

$maxAttempts = 60
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
	try {
		$response = Invoke-WebRequest -Uri 'https://127.0.0.1:8081/' -SkipCertificateCheck -Method Get -TimeoutSec 5
		if ($response.StatusCode -in 200, 401) {
			Write-Host 'Cosmos DB Emulator is ready on Windows.'
			exit 0
		}
	}
	catch {
		Write-Host "Cosmos DB Emulator is not ready yet (attempt $attempt/$maxAttempts)."
	}

	Start-Sleep -Seconds 5
}

throw 'Cosmos DB Emulator failed to become ready on Windows.'
