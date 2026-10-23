# 8. Resultados del mantenimiento y refactorización

## Resultados funcionales

- La entrega se ejecuta con SQLite.
- No requiere MySQL ni `mysqlclient`.
- La instalación crea un entorno virtual nuevo.
- El seed genera datos ficticios para la exposición.
- El seed es idempotente y no duplica stock.
- El mantenimiento crea respaldos `.sqlite3` válidos.
- Los archivos inválidos son rechazados.
- El reinicio elimina operaciones y conserva catálogos y usuarios.
- La interfaz funciona con recursos locales.

## Resultados de calidad

- 34 pruebas automatizadas aprobadas.
- Migraciones sin cambios pendientes.
- Dependencias verificadas con `pip check`.
- Login y recursos estáticos comprobados con HTTP 200.
- Se agregó protección contra fórmulas maliciosas en reportes Excel.
- Se excluyeron tokens, bases reales, respaldos y datos personales.

## Evidencias

```text
System check identified no issues (0 silenced).
No changes detected
No broken requirements found.
Ran 34 tests
OK
```

## Archivos principales modificados

- `veterinaria/settings.py`.
- `apps/core/maintenance.py`.
- `apps/core/views.py`.
- `apps/core/management/commands/seed_demo.py`.
- `requirements.txt`.
- `templates/core/mantenimiento.html`.
