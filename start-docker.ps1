$ErrorActionPreference = 'Stop'

$certDirectory = Join-Path $env:USERPROFILE '.aspnet\https'
$certPath = Join-Path $certDirectory 'ecommerce-backend-docker.pfx'
New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null

$certPassword = [guid]::NewGuid().ToString('N')
dotnet dev-certs https -ep $certPath -p $certPassword
if ($LASTEXITCODE -ne 0) { throw 'Could not export the HTTPS certificate.' }

dotnet dev-certs https --trust
if ($LASTEXITCODE -ne 0) { throw 'Could not trust the HTTPS certificate.' }

Push-Location $PSScriptRoot
try {
    $secretLines = @(dotnet user-secrets list --json --project '.\E-commerce-backend.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Could not read .NET user-secrets.' }
    $secretJson = ($secretLines | Where-Object { $_ -notmatch '^//(BEGIN|END)' }) -join "`n"
    $secrets = $secretJson | ConvertFrom-Json -AsHashtable

    foreach ($key in @('Supabase:Url', 'Supabase:PublishableKey', 'ConnectionStrings:SupabaseDatabase')) {
        if ([string]::IsNullOrWhiteSpace($secrets[$key])) {
            throw "Missing user-secret: $key"
        }
    }

    $envFile = Join-Path $env:TEMP ("ecommerce-backend-" + [guid]::NewGuid().ToString('N') + '.env')
    @(
        "Supabase__Url=$($secrets['Supabase:Url'])"
        "Supabase__PublishableKey=$($secrets['Supabase:PublishableKey'])"
        "ConnectionStrings__SupabaseDatabase=$($secrets['ConnectionStrings:SupabaseDatabase'])"
    ) | Set-Content -LiteralPath $envFile -Encoding utf8NoBOM

    docker build -t ecommerce-backend .
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the Docker image.' }

    Write-Host 'API: https://localhost:3100/'
    docker run --rm --name ecommerce-backend-docker --env-file $envFile -p 3100:8443 -e ASPNETCORE_URLS=https://+:8443 -e ASPNETCORE_Kestrel__Certificates__Default__Path=/https/ecommerce-backend-docker.pfx -e ASPNETCORE_Kestrel__Certificates__Default__Password=$certPassword -e DataProtection__KeysPath=/app/keys -v "${certDirectory}:/https:ro" -v ecommerce-backend-keys:/app/keys ecommerce-backend
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the Docker container.' }
}
finally {
    if ($envFile -and (Test-Path -LiteralPath $envFile)) {
        Remove-Item -LiteralPath $envFile -Force
    }
    Pop-Location
}
