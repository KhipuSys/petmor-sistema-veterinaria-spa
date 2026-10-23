# Petmor Final

Sistema local de gestión para veterinaria y spa desarrollado con Python, Django 5.2 y SQLite. Incluye clientes, mascotas, agenda, historias clínicas, vacunas, telemedicina, grooming, proveedores, compras, inventario, kardex, facturación, caja y reportes.

## Requisitos

- Windows 10 u 11.
- Python 3.10 o superior instalado con el lanzador `py`.
- Internet solamente durante la primera instalación de dependencias.

No requiere MySQL, XAMPP, Node.js ni conexión a Internet durante el uso normal. La consulta opcional de DNI/RUC mediante Factiliza sí necesita Internet y una credencial propia.

## Primera instalación

1. Ejecutar `CONFIGURAR.bat`.
2. Esperar a que termine la instalación, las migraciones y la carga de demostración.
3. Ejecutar `INICIAR.bat`.

El navegador abrirá `http://127.0.0.1:8000/login/`.

Credenciales locales de demostración:

```text
Usuario: admin_demo
Contraseña: PetmorDemo2026!
```

Estas credenciales son públicas y solo sirven para esta entrega local. La contraseña puede cambiarse antes de configurar mediante la variable `PETMOR_DEMO_PASSWORD`.

## Uso posterior

Ejecutar `INICIAR.bat`. No es necesario reinstalar dependencias ni cargar datos en cada inicio.

Para cerrar el servidor, volver a la ventana de comandos y presionar `Ctrl+C`.

## Pruebas

Ejecutar `PROBAR.bat`. El script comprueba configuración, migraciones pendientes y toda la suite de pruebas.

## Mantenimiento

La sección **Sistema > Mantenimiento** está disponible únicamente para superusuarios.

- **Respaldo:** descarga una copia consistente con extensión `.sqlite3`.
- **Restauración:** acepta solamente bases SQLite válidas de Petmor, comprueba integridad y crea un respaldo preventivo.
- **Reinicio:** elimina información transaccional, pero conserva usuarios y catálogos.

La base local se guarda como `db.sqlite3`. Los respaldos quedan en `backups/`; ambos están excluidos de Git.

## Configuración opcional

```powershell
$env:PETMOR_DEMO_PASSWORD = 'OtraClaveSegura!'
$env:DJANGO_SECRET_KEY = 'clave-local-personalizada'
$env:DJANGO_DEBUG = '1'
$env:FACTILIZA_TOKEN = 'token-propio'
```

No se incluye ningún token, respaldo ni dato personal del proyecto original.

## Documentación T2

- `docs/DIAGNOSTICO_Y_REFACTORIZACION.md`
- `docs/CONFIGURACION.md`
- `docs/RFC-001-MIGRACION-LOCAL-SQLITE.md`
- `docs/PLAN_DE_PRUEBAS_Y_CAPTURAS.md`
- `docs/TDD_EVIDENCIA.md`

## Alcance

Esta configuración está diseñada para demostración y evaluación local en `127.0.0.1`. No representa una configuración de producción ni implementa emisión electrónica SUNAT.
