param([string]$PgBin = 'C:/Program Files/PostgreSQL/17/bin')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reportDir = Join-Path $repoRoot ('.local/test-results/dynamic-promotions-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $pgPort = $listener.LocalEndpoint.Port; $listener.Stop()
$pgRoot = Join-Path $reportDir 'pg'
$previousConnection = $env:ECOMMERCE_TEST_DATABASE
$started = $false
$pgArgs = @('-h','127.0.0.1','-p',"$pgPort",'-U','postgres')
function Check([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed. See $reportDir" } }
function Bootstrap([string]$database) {
    & (Join-Path $PgBin 'createdb.exe') @pgArgs --template=template0 --locale-provider=icu --icu-locale=en-US $database
    Check 'create database'
    & (Join-Path $PgBin 'psql.exe') @pgArgs -d $database -X -q -v ON_ERROR_STOP=1 -f tests/performance/bootstrap-local-postgres.sql | Out-Null
    Check 'bootstrap'
}
function Sql([string]$database,[string]$statement) {
    $result = & (Join-Path $PgBin 'psql.exe') @pgArgs -d $database -X -q -A -t -v ON_ERROR_STOP=1 -c $statement
    Check 'SQL'
    return ($result -join "`n").Trim()
}
function Migrate([string]$database,[string]$target = '',[string]$expectedFailure = '') {
    $cs = "Host=127.0.0.1;Port=$pgPort;Database=$database;Username=postgres"
    $arguments = @('ef','database','update')
    if ($target) { $arguments += $target }
    $arguments += @('--configuration','Release','--no-build','--connection',$cs)
    & dotnet @arguments *> (Join-Path $reportDir 'migrations.log')
    if ($expectedFailure) {
        if ($LASTEXITCODE -eq 0 -or -not (Select-String -LiteralPath (Join-Path $reportDir 'migrations.log') -SimpleMatch $expectedFailure -Quiet)) { throw 'Expected safe Down refusal' }
    } else { Check 'migration' }
}
Push-Location $repoRoot
try {
    & (Join-Path $PgBin 'initdb.exe') -D $pgRoot -U postgres --auth=trust --no-locale --encoding=UTF8 *> (Join-Path $reportDir 'initdb.log')
    Check 'initdb'
    & (Join-Path $PgBin 'pg_ctl.exe') -D $pgRoot -l (Join-Path $reportDir 'server.log') -o "-h 127.0.0.1 -p $pgPort" -w start
    Check 'start'; $started = $true
    Bootstrap 'ecomm_test_dynamic'
    dotnet build E-commerce-backend.slnx --configuration Release --no-restore *> (Join-Path $reportDir 'build.log')
    Check 'build'
    Migrate 'ecomm_test_dynamic'
    $env:ECOMMERCE_TEST_DATABASE = "Host=127.0.0.1;Port=$pgPort;Database=ecomm_test_dynamic;Username=postgres"
    dotnet test E-commerce-backend.slnx --configuration Release --no-build --logger 'trx;LogFileName=dynamic.trx' --results-directory $reportDir *> (Join-Path $reportDir 'tests.log')
    Check 'full tests'
    Bootstrap 'ecomm_test_dynamic_migration'
    Migrate 'ecomm_test_dynamic_migration' 'AddInventoryReservations'
    Sql 'ecomm_test_dynamic_migration' @'
BEGIN;
INSERT INTO app.categories(id,display_name,slug) VALUES ('ab000000-0000-0000-0000-000000000001','Fixture','fixture');
INSERT INTO app.products(id,name,slug,status) VALUES ('ab000000-0000-0000-0000-000000000002','Fixture','fixture','Draft');
INSERT INTO app.product_variants(id,product_id,sku,status,image_urls) VALUES ('ab000000-0000-0000-0000-000000000003','ab000000-0000-0000-0000-000000000002','FIXTURE','Draft',ARRAY[]::text[]);
INSERT INTO app.product_categories(product_id,category_id) VALUES ('ab000000-0000-0000-0000-000000000002','ab000000-0000-0000-0000-000000000001');
INSERT INTO app.variant_price_changes(id,variant_id,base_amount,selling_amount,effective_at,reason) VALUES ('ab000000-0000-0000-0000-000000000004','ab000000-0000-0000-0000-000000000003',99.99,99.99,now()-interval '20 days','Fixture');
COMMIT;
'@ | Out-Null
    $digest = 'SELECT md5(string_agg(row_to_json(p)::text, '''' ORDER BY p.id)) FROM app.variant_price_changes p'
    $before = Sql 'ecomm_test_dynamic_migration' $digest
    Migrate 'ecomm_test_dynamic_migration'
    if ((Sql 'ecomm_test_dynamic_migration' $digest) -ne $before) { throw 'Up changed historical price fields' }
    Migrate 'ecomm_test_dynamic_migration' 'AddInventoryReservations'
    Migrate 'ecomm_test_dynamic_migration'
    if ((Sql 'ecomm_test_dynamic_migration' $digest) -ne $before) { throw 'Down/Up changed historical price fields' }
    Sql 'ecomm_test_dynamic_migration' "INSERT INTO app.promotions(id,scope,name,discount_type,discount_value,status,version,created_at,updated_at) VALUES ('ab000000-0000-0000-0000-000000000005','catalog','Protected','percentage',10,'Draft',1,now(),now())" | Out-Null
    Migrate 'ecomm_test_dynamic_migration' 'AddInventoryReservations' 'Dynamic promotion history exists'
    dotnet ef migrations has-pending-model-changes --configuration Release --no-build *> (Join-Path $reportDir 'model.log')
    Check 'model snapshot'
    Get-Content (Join-Path $reportDir 'build.log') | Select-Object -Last 6
    Get-Content (Join-Path $reportDir 'tests.log') | Select-Object -Last 5
    Write-Output 'Verified: fresh migration, upgrade with price history, Down/Up digest, safe Down refusal and EF snapshot.'
}
finally {
    if ($started) { & (Join-Path $PgBin 'pg_ctl.exe') -D $pgRoot -m fast -w stop | Out-Null }
    $env:ECOMMERCE_TEST_DATABASE = $previousConnection
    Pop-Location
    Write-Output "PROMOTIONS_REPORT=$reportDir"
}
