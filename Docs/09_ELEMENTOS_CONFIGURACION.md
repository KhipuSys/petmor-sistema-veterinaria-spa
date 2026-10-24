# 9. Elementos de configuracion del proyecto

| Elemento | Ubicacion | Proposito |
|---|---|---|
| Proyecto | `Petmor.Web.csproj` | Target .NET 9 y paquetes |
| Entrada | `Program.cs` | DI, Identity, middleware y rutas |
| Configuracion | `appsettings.json` | Conexion a `Petmor_BD` |
| Secretos | User Secrets `petmor-local` | Token Factiliza sin versionar |
| Datos | `Data/` | Contexto y actualizaciones |
| Migraciones | `Migrations/` | Modelo inicial de EF Core |
| Presentacion | `Views/` | Interfaz Razor |
| Estaticos | `wwwroot/` | CSS, JavaScript e imagenes |
| Pruebas | `Tests/` | Pruebas de servicios y web |
| Arranque | `ejecutar.cmd` | Preparacion y ejecucion local |
| Base | `preparar-petmor.ps1` | Crea `Petmor_BD` si no existe |

## Valores que no deben publicarse

Tokens, contrasenas, archivos `.publishSettings`, cadenas de produccion,
respaldos de bases de datos y datos reales de clientes o mascotas.
