# Evidencia TDD y verificación

## Alcance

Las pruebas se derivaron del objetivo de entregar Petmor como aplicación local reproducible con datos ficticios, SQLite, mantenimiento seguro y recursos frontend locales.

## Ciclos RED/GREEN

| Comportamiento | Evidencia RED | Implementación | Evidencia GREEN |
| --- | --- | --- | --- |
| Carga demo completa e idempotente | `Unknown command: 'seed_demo'` en 2 pruebas | `apps/core/management/commands/seed_demo.py` | 2 pruebas aprobadas |
| Mantenimiento desacoplado y compatible con SQLite | `ModuleNotFoundError: apps.core.maintenance` | `apps/core/maintenance.py` y vistas delgadas | 7 pruebas aprobadas |
| Exportación Excel no interpreta datos como fórmulas | `ImportError: excel_safe_text` | Sanitización de texto antes de escribir celdas | 2 pruebas aprobadas |

Durante la validación de mantenimiento, una prueba detectó archivos SQLite bloqueados en Windows. Se corrigió cerrando explícitamente las conexiones nativas después del backup y la validación.

## Garantías

| Garantía | Prueba | Tipo | Resultado |
| --- | --- | --- | --- |
| El seed crea usuarios y datos para todos los módulos principales | `apps/core/test_seed_demo.py` | Integración | PASS |
| Ejecutar el seed dos veces no duplica registros ni stock | `test_seed_is_idempotent_for_counts_and_stock` | Integración | PASS |
| Backup generado es una base SQLite íntegra y compatible | `test_backup_is_a_valid_sqlite_database` | Integración | PASS |
| Archivos falsos o esquemas incompletos se rechazan | `test_invalid_backup_is_rejected`, `test_incomplete_sqlite_schema_is_rejected` | Seguridad | PASS |
| Reset elimina operaciones, pone stock en cero y permite regenerar la demo | `test_reset_removes_operations_but_keeps_catalogs_and_users` | Integración | PASS |
| Usuarios no administradores no acceden a mantenimiento | `test_regular_user_cannot_open_maintenance_page` | Autorización | PASS |
| Pantallas principales responden para el administrador demo | `apps/core/test_smoke.py` | Smoke | PASS |
| Templates no dependen de CDN y los archivos locales existen | `test_templates_use_local_frontend_assets` | Regresión | PASS |
| Texto peligroso se exporta como texto y no como fórmula Excel | `apps/reportes/tests.py` | Seguridad | PASS |
| Compras, kardex, ventas, caja y Factiliza simulada no regresan | `apps/compras/tests.py` | Integración | PASS |

## Comandos finales

```powershell
.\.venv\Scripts\python.exe manage.py check
.\.venv\Scripts\python.exe manage.py makemigrations --check --dry-run
.\.venv\Scripts\python.exe -m pip check
.\.venv\Scripts\python.exe manage.py test
```

Resultados finales:

```text
System check identified no issues (0 silenced).
No changes detected
No broken requirements found.
Ran 34 tests
OK
```

La contraseña demo configurable también se verificó ejecutando las pruebas de seed y smoke con `PETMOR_DEMO_PASSWORD` personalizada: 4 pruebas aprobadas.

## Riesgos residuales

- La restauración valida integridad, tablas y migraciones, pero no compara cada columna del esquema.
- Factiliza requiere Internet y una credencial externa; sin ella, el ingreso es manual.
- La entrega es local y no representa una configuración productiva multiusuario.
