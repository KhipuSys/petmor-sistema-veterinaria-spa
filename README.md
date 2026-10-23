# Petmor Sistema Veterinaria Spa

Aplicación web ASP.NET Core para la gestión de veterinaria, spa, clientes,
mascotas, inventario, ventas, compras, caja y reportes.

## Requisitos

- .NET SDK 9
- SQL Server Express con la instancia `SQLEXPRESS` iniciada
- PowerShell, incluido en Windows

## Ejecución local

En Windows, abre `ejecutar.cmd` desde la carpeta del proyecto. El script crea o
verifica `Petmor_BD`, restaura dependencias, inicia Petmor y abre automáticamente
`http://localhost:5000` en el navegador.

El primer inicio crea el usuario administrador:

- Usuario: `admin`
- Contraseña: `Admin123*`

También puedes ejecutar manualmente:

```powershell
dotnet restore
dotnet run
```

La configuración local usa `appsettings.json`. Los secretos, como el token de
Factiliza, deben configurarse mediante User Secrets y nunca dentro del repositorio.
La base de datos local configurada es `Petmor_BD` en `localhost\SQLEXPRESS`.

## Pruebas

```powershell
dotnet test Tests/Comercial.IntegrationTests.csproj --no-restore
```
