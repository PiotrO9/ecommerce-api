param([string]$PgBin = 'C:/Program Files/PostgreSQL/17/bin')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportDir = Join-Path $repoRoot ('.local/test-results/inventory-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $pgPort = $listener.LocalEndpoint.Port; $listener.Stop()
$pgRoot = Join-Path $env:TEMP ('ecomm-inventory-pg-' + [guid]::NewGuid().ToString('N'))
$testDatabase = 'ecomm_test_inventory'
$testConnection = "Host=127.0.0.1;Port=$pgPort;Database=$testDatabase;Username=postgres"
$previousTestConnection = $env:ECOMMERCE_TEST_DATABASE
$pgStarted = $false
function CheckExit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed ($LASTEXITCODE); see $reportDir" } }
Push-Location $repoRoot
try {
    & (Join-Path $PgBin 'initdb.exe') -D $pgRoot -U postgres --auth=trust --no-locale --encoding=UTF8 *> (Join-Path $reportDir 'initdb.log')
    CheckExit 'initdb'
    & (Join-Path $PgBin 'pg_ctl.exe') -D $pgRoot -l (Join-Path $pgRoot 'server.log') -o "-h 127.0.0.1 -p $pgPort" -w start
    CheckExit 'PostgreSQL startup'; $pgStarted = $true
    "CREATE DATABASE $testDatabase TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER icu ICU_LOCALE 'en-US';" |
        & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d postgres -v ON_ERROR_STOP=1
    CheckExit 'create database'
    & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d $testDatabase -f tests/performance/bootstrap-local-postgres.sql
    CheckExit 'bootstrap'
    dotnet build E-commerce-backend.slnx --configuration Release --no-restore *> (Join-Path $reportDir 'build.log')
    CheckExit 'build'
    dotnet ef database update --configuration Release --no-build --connection $testConnection *> (Join-Path $reportDir 'migrations.log')
    CheckExit 'migrate from zero'
    $env:ECOMMERCE_TEST_DATABASE = $testConnection
    dotnet test E-commerce-backend.slnx --configuration Release --no-build --logger "trx;LogFileName=inventory.trx" --results-directory $reportDir *> (Join-Path $reportDir 'tests.log')
    CheckExit 'full test suite'
    $historySql = "SELECT md5(COALESCE(string_agg(row_to_json(p)::text, '' ORDER BY id),'')) FROM app.variant_price_changes p"
    $historyBefore = & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d $testDatabase -Atc $historySql
    CheckExit 'history snapshot'
    dotnet ef database update AddCatalogPublicationRules --configuration Release --no-build --connection $testConnection *> (Join-Path $reportDir 'down.log')
    CheckExit 'empty inventory Down'
    dotnet ef database update --configuration Release --no-build --connection $testConnection *> (Join-Path $reportDir 'upgrade.log')
    CheckExit 'inventory Up on existing catalog'
    $historyAfter = & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d $testDatabase -Atc $historySql
    CheckExit 'history comparison'
    if ($historyBefore -ne $historyAfter) { throw 'Historical price records changed during Down/Up.' }
    dotnet ef migrations has-pending-model-changes --configuration Release --no-build *> (Join-Path $reportDir 'model.log')
    CheckExit 'EF model snapshot'
    Get-Content (Join-Path $reportDir 'build.log') | Select-Object -Last 6
    Get-Content (Join-Path $reportDir 'tests.log') | Select-Object -Last 9
    Write-Output 'Fresh migration, existing catalog Down/Up, historical price hash and EF model snapshot verified.'
}
finally {
    if ($pgStarted) { & (Join-Path $PgBin 'pg_ctl.exe') -D $pgRoot -m fast -w stop | Out-Null }
    $env:ECOMMERCE_TEST_DATABASE = $previousTestConnection
    Pop-Location
    Write-Output "INVENTORY_REPORT=$reportDir"
}
