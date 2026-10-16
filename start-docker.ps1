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
    docker build -t ecommerce-backend .
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the Docker image.' }

    Write-Host 'API: https://localhost:3100/'
    docker run --rm --name ecommerce-backend-docker -p 3100:8443 -e ASPNETCORE_URLS=https://+:8443 -e ASPNETCORE_Kestrel__Certificates__Default__Path=/https/ecommerce-backend-docker.pfx -e ASPNETCORE_Kestrel__Certificates__Default__Password=$certPassword -v "${certDirectory}:/https:ro" ecommerce-backend
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the Docker container.' }
}
finally {
    Pop-Location
}
