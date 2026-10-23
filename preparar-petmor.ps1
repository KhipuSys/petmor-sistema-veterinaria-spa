$ErrorActionPreference = 'Stop'

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
    Write-Host 'Instala SQL Server Express y asegúrate de que el servicio este iniciado.' -ForegroundColor Yellow
    exit 1
}
finally {
    if ($connection.State -eq 'Open') { $connection.Close() }
}
