# Petmor Sistema Veterinaria Spa

Aplicación web ASP.NET Core para la gestión de veterinaria, spa, clientes,
mascotas, inventario, ventas, compras, caja y reportes.

## Requisitos

- .NET SDK 9
- SQL Server local para desarrollo

## Ejecución local

En Windows, abre `ejecutar.cmd` desde la carpeta del proyecto.

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
