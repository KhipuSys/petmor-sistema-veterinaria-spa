# Elementos de configuración

## Código fuente

| Elemento | Ruta | Propósito |
| --- | --- | --- |
| Entry point | `manage.py` | Comandos Django |
| Configuración | `veterinaria/settings.py` | Apps, SQLite, idioma, estáticos y seguridad local |
| Rutas principales | `veterinaria/urls.py` | Enrutamiento de módulos |
| Aplicaciones | `apps/` | Clínica, spa, ERP, usuarios y mantenimiento |
| Plantillas | `templates/` | Interfaz renderizada en servidor |
| Recursos | `static/` | CSS, JS, logo y librerías locales |
| Migraciones | `apps/*/migrations/` | Versionamiento del esquema |
| Dependencias | `requirements.txt` | Paquetes Python |

## Artefactos locales

| Elemento | Creación | Tratamiento |
| --- | --- | --- |
| `.venv/` | `CONFIGURAR.bat` | No versionar |
| `db.sqlite3` | Migraciones | No versionar |
| `backups/` | Mantenimiento | No versionar |
| `media/` | Cargas del usuario | No versionar |
| `staticfiles/` | `collectstatic` | Regenerable |

## Variables

| Variable | Obligatoria | Uso |
| --- | --- | --- |
| `PETMOR_DEMO_PASSWORD` | No | Sustituye contraseña demo durante el seed |
| `DJANGO_SECRET_KEY` | No | Sustituye clave local predeterminada |
| `DJANGO_DEBUG` | No | `1` activa modo local |
| `FACTILIZA_TOKEN` | No | Habilita consulta externa de DNI/RUC |

## Versiones

- Python: 3.10 o superior.
- Django: rama 5.2.
- Bootstrap: 5.3.0.
- Font Awesome Free: 6.4.0.
- Chart.js: 4.4.7.
- Base: SQLite incluida en Python.

## Control de configuración

Los cambios deben solicitarse mediante RFC, revisarse, probarse en una copia y aprobarse antes de incorporarse a la entrega. Bases, respaldos, credenciales y datos de pacientes no forman parte del elemento de configuración versionado.
