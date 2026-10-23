# 9. Elementos de configuración del proyecto

## Elementos controlados

| Elemento | Ubicación | Descripción |
| --- | --- | --- |
| Código Django | `apps/`, `veterinaria/` | Lógica de negocio y configuración |
| Plantillas | `templates/` | Interfaz web |
| Estáticos | `static/` | CSS, JavaScript, iconos y librerías |
| Migraciones | `apps/*/migrations/` | Esquema de la base |
| Dependencias | `requirements.txt` | Versiones exactas de paquetes |
| Scripts | `CONFIGURAR.bat`, `INICIAR.bat`, `PROBAR.bat` | Operación local |
| Documentación | `docs/` | Diseño, cambios y pruebas |

## Archivos generados

- `.venv/`: entorno virtual local.
- `db.sqlite3`: base de datos local.
- `media/`: archivos subidos.
- `backups/`: respaldos de mantenimiento.
- `staticfiles/`: salida regenerable de `collectstatic`.

Estos archivos están excluidos mediante `.gitignore`.

## Variables de entorno

- `PETMOR_DEMO_PASSWORD`: cambia la contraseña del usuario demo.
- `DJANGO_SECRET_KEY`: clave local opcional.
- `DJANGO_DEBUG`: activa o desactiva debug local.
- `FACTILIZA_TOKEN`: habilita consultas externas de DNI/RUC.

## Línea base

La configuración aprobada para esta evaluación es Django 5.2, Python 3.10+, SQLite, idioma español peruano, zona horaria `America/Lima` y ejecución en `127.0.0.1:8000`.
