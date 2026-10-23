# Plan de pruebas y capturas T2

## Pruebas automáticas

Ejecutar `PROBAR.bat` y conservar una captura del resultado final.

| Área | Evidencia automática |
| --- | --- |
| Configuración | `manage.py check` |
| Esquema | `makemigrations --check --dry-run` |
| Datos demo | Creación completa e idempotencia |
| Mantenimiento | Estado, backup, validación y reset |
| ERP | Compras, kardex, ventas, caja y transacciones |
| Pantallas | Smoke test de rutas principales |
| Offline | Ausencia de CDN y archivos locales presentes |

## Recorrido manual

1. Ejecutar `INICIAR.bat` e iniciar sesión.
2. Revisar indicadores y gráfico del dashboard.
3. Abrir Clientes y el perfil de una mascota.
4. Abrir Agenda, Historia Clínica, Vacunaciones y Telemedicina.
5. Abrir Órdenes de spa y Catálogo de servicios.
6. Revisar Proveedores, Compras, Inventario y Kardex.
7. Revisar Facturación y Caja.
8. Exportar al menos un PDF y un Excel.
9. Descargar un respaldo SQLite desde Mantenimiento.
10. Desconectar Internet, recargar login/dashboard y comprobar estilos, iconos y gráfico.

## Capturas sugeridas para el punto 12

- Login de Petmor.
- Dashboard con indicadores.
- Perfil de cliente y mascota.
- Agenda de citas.
- Historia clínica o vacunación.
- Orden de grooming.
- Inventario y kardex.
- Compra registrada.
- Facturación y caja.
- Reportes.
- Mantenimiento SQLite.

No mostrar claves, tokens, consola con rutas personales ni información real durante la exposición.
