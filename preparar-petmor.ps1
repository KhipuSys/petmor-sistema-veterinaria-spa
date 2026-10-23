$ErrorActionPreference = 'Stop'

try {
    $service = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
    if ($service.Status -ne 'Running') {
        Write-Host 'El servicio SQL Server Express esta detenido. Intentando iniciarlo...'
        Start-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
    }
}
catch {
    if (-not $service) {
        Write-Host 'No se encontro la instancia SQLEXPRESS.' -ForegroundColor Red
        Write-Host 'Instala SQL Server Express con el nombre de instancia SQLEXPRESS.' -ForegroundColor Yellow
    }
    else {
        Write-Host 'No se pudo iniciar el servicio SQL Server Express.' -ForegroundColor Red
        Write-Host 'Abre Servicios de Windows y activa MSSQL$SQLEXPRESS.' -ForegroundColor Yellow
    }
    exit 1
}

$connection = New-Object System.Data.SqlClient.SqlConnection 'Server=localhost\SQLEXPRESS;Database=master;Integrated Security=True;TrustServerCertificate=True'
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "IF DB_ID(N'Petmor_BD') IS NULL CREATE DATABASE [Petmor_BD];"
    [void]$command.ExecuteNonQuery()
    Write-Host 'Base de datos Petmor_BD lista.'
}
catch {
    Write-Host 'No se pudo conectar a localhost\SQLEXPRESS.' -ForegroundColor Red
    Write-Host 'Verifica que SQL Server Express este instalado y que la instancia se llame SQLEXPRESS.' -ForegroundColor Yellow
    exit 1
}
finally {
    if ($connection.State -eq 'Open') { $connection.Close() }
}
