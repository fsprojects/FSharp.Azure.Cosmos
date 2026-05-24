$emulatorPath = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
if (Test-Path $emulatorPath) {
	"installed=true" >> $env:GITHUB_OUTPUT
	Write-Host 'Azure Cosmos DB Emulator is already installed.'
	exit 0
}

choco install azure-cosmosdb-emulator -y --no-progress --install-arguments="'/l*v C:\azure-cosmosdb-emulator_msi_install.log'"
if ($LASTEXITCODE -eq 0 -and (Test-Path $emulatorPath)) {
	"installed=true" >> $env:GITHUB_OUTPUT
	Write-Host 'Azure Cosmos DB Emulator installed successfully.'
}
else {
	"installed=false" >> $env:GITHUB_OUTPUT
	Write-Warning 'Azure Cosmos DB Emulator installation failed. Continuing because Windows job runs build-only target.'
	if (Test-Path 'C:\azure-cosmosdb-emulator_msi_install.log') {
		Write-Host 'MSI log tail:'
		Get-Content 'C:\azure-cosmosdb-emulator_msi_install.log' -Tail 120
	}
}
