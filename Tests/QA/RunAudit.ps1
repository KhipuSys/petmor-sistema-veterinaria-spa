$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$database = 'Petmor_QA_' + [Guid]::NewGuid().ToString('N')
$server = $null
$exitCode = 1
$saved = @{}
$settings = @{
    'ConnectionStrings__DefaultConnection' = "Server=localhost\SQLEXPRESS;Database=$database;Trusted_Connection=True;TrustServerCertificate=True"
    'ASPNETCORE_ENVIRONMENT' = 'Production'
    'FactilizaSettings__Token' = 'QA-NO-REAL-CREDENTIAL'
    'GoogleAI__ApiKey' = 'QA-NO-REAL-CREDENTIAL'
    'HTTPS_PROXY' = 'http://127.0.0.1:1'
    'HTTP_PROXY' = 'http://127.0.0.1:1'
    'NO_PROXY' = 'localhost,127.0.0.1'
}
try {
    & dotnet run --project (Join-Path $root 'Tests\Comercial.IntegrationTests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas SQL fallaron.' }
    & npm.cmd ci --prefix $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron instalar las herramientas QA.' }
    if (Get-NetTCPConnection -LocalPort 5201 -State Listen -ErrorAction SilentlyContinue) { throw 'El puerto 5201 ya esta ocupado.' }
    foreach ($key in $settings.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process')
    }
    $server = Start-Process dotnet -WorkingDirectory $root -ArgumentList 'run --project Petmor.Web.csproj --no-build -c Release --no-launch-profile --urls http://127.0.0.1:5201' -PassThru
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        try {
            $r = Invoke-WebRequest -UseBasicParsing 'http://127.0.0.1:5201/Identity/Account/Login' -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $ready=$true; break }
        } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'El servidor QA no inicio.' }
    & npm.cmd test --prefix $PSScriptRoot
    $exitCode = $LASTEXITCODE
} finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    if ($server) { & taskkill /PID $server.Id /T /F }
    # Solo se elimina el nombre aleatorio creado por esta ejecucion.
    if ($database -match '^Petmor_QA_[a-f0-9]{32}$') {
        $connection = New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True'
        try {
            $connection.Open()
            $command = $connection.CreateCommand()
            $command.CommandText = "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END"
            [void]$command.ExecuteNonQuery()
        } finally { $connection.Dispose() }
    }
}
exit $exitCode
