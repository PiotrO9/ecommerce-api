param(
    [string]$PgBin = 'C:/Program Files/PostgreSQL/17/bin'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runDir = Join-Path $root ('.local/test-results/shopping-live-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
function FreePort {
    $socket = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $socket.Start(); $selected = $socket.LocalEndpoint.Port; $socket.Stop(); return $selected
}
$pgPort = FreePort
$apiPort = FreePort
while ($apiPort -eq $pgPort) { $apiPort = FreePort }
$pgRoot = Join-Path $env:TEMP ('ecomm-shopping-live-pg-' + [guid]::NewGuid().ToString('N'))
$database = 'ecomm_test_shopping_live'
$cs = "Host=127.0.0.1;Port=$pgPort;Database=$database;Username=postgres"
$apiRoot = "https://localhost:$apiPort"
$pgStarted = $false
$apiProcess = $null
$environmentNames = @('ASPNETCORE_ENVIRONMENT','ConnectionStrings__SupabaseDatabase','DemoSeed__Enabled','DemoSeed__Key','ECOMMERCE_SMOKE_SEED_KEY','DataProtection__KeysPath')
$previousEnvironment = @{}
foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location $root
try {
    & (Join-Path $PgBin 'initdb.exe') -D $pgRoot -U postgres --auth=trust --no-locale --encoding=UTF8 *> (Join-Path $runDir 'initdb.log')
    if ($LASTEXITCODE -ne 0) { throw 'Temporary initdb failed; see initdb.log.' }
    & (Join-Path $PgBin 'pg_ctl.exe') -D $pgRoot -l (Join-Path $pgRoot 'server.log') -o "-h 127.0.0.1 -p $pgPort" -w start
    if ($LASTEXITCODE -ne 0) { throw 'Temporary PostgreSQL failed to start.' }
    $pgStarted = $true
    "CREATE DATABASE $database TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER icu ICU_LOCALE 'en-US';" |
        & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d postgres -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated database.' }
    & (Join-Path $PgBin 'psql.exe') -h 127.0.0.1 -p $pgPort -U postgres -d $database -f tests/performance/bootstrap-local-postgres.sql
    if ($LASTEXITCODE -ne 0) { throw 'Local auth bootstrap failed.' }
    dotnet build E-commerce-backend.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet ef database update --connection $cs --no-build *> (Join-Path $runDir 'migrations.log')
    if ($LASTEXITCODE -ne 0) { throw 'Local migrations failed; see migrations.log.' }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__SupabaseDatabase = $cs
    $env:DemoSeed__Enabled = 'true'
    $env:DemoSeed__Key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:ECOMMERCE_SMOKE_SEED_KEY = $env:DemoSeed__Key
    $env:DataProtection__KeysPath = Join-Path $pgRoot 'api-dp-keys'
    $dll = Join-Path $root 'bin/Debug/net10.0/E-commerce-backend.dll'
    $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @("`"$dll`"",'--urls',$apiRoot) -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runDir 'api.stdout.log') -RedirectStandardError (Join-Path $runDir 'api.stderr.log') -PassThru
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.HasExited) { throw 'API exited during startup; see api.stderr.log.' }
        try {
            $health = Invoke-WebRequest -Uri "$apiRoot/" -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 2
            if ([int]$health.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (!$ready) { throw 'HTTPS API startup timed out.' }
    Write-Output 'Isolated HTTPS API ready; starting real Supabase Auth acceptance checks.'
    & (Join-Path $PSScriptRoot 'shopping.ps1') -Api "$apiRoot/api/v1/web" -Database $database -PgPort $pgPort -Psql (Join-Path $PgBin 'psql.exe') -ReportPath (Join-Path $runDir 'report.json')
}
finally {
    if ($apiProcess -and !$apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id; $apiProcess.WaitForExit() }
    if ($pgStarted) {
        $resolved = (Resolve-Path -LiteralPath $pgRoot).Path
        $tempPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        if (!$resolved.StartsWith($tempPrefix,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'ecomm-shopping-live-pg-*') { throw 'Unexpected temporary server path.' }
        & (Join-Path $PgBin 'pg_ctl.exe') -D $resolved -m fast -w stop
        if ($LASTEXITCODE -ne 0) { throw 'Cannot stop own temporary PostgreSQL.' }
    }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
    Pop-Location
    Write-Output "LIVE_REPORT=$runDir"
}
