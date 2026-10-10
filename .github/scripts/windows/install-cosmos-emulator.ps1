# Installs the Windows Azure Cosmos DB Emulator unless it is installed already, as it is on the hosted Windows runners.
# Fails when the installation fails.
$emulatorPath = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
if (Test-Path $emulatorPath) {
	Write-Host 'Azure Cosmos DB Emulator is already installed.'
	exit 0
}

choco install azure-cosmosdb-emulator -y --no-progress --install-arguments="'/l*v C:\azure-cosmosdb-emulator_msi_install.log'"
if ($LASTEXITCODE -eq 0 -and (Test-Path $emulatorPath)) {
	Write-Host 'Azure Cosmos DB Emulator installed successfully.'
	exit 0
}

if (Test-Path 'C:\azure-cosmosdb-emulator_msi_install.log') {
	Write-Host 'MSI log tail:'
	Get-Content 'C:\azure-cosmosdb-emulator_msi_install.log' -Tail 120
}

Write-Error 'Azure Cosmos DB Emulator installation failed.'
exit 1
