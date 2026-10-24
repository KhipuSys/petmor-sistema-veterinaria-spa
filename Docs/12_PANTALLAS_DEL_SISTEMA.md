# 12. Pantallas del sistema

Las capturas deben tomarse sobre la version actual ejecutada desde
`http://localhost:5000`, usando la base `Petmor_BD`.

## Catalogo de evidencias

| Codigo | Pantalla | Evidencia esperada |
|---|---|---|
| P01 | Login | Marca Petmor y acceso administrativo |
| P02 | Dashboard | Resumen de mascotas, atenciones y operaciones |
| P03 | Clientes | Listado y busqueda de propietarios |
| P04 | Mascotas | Ficha clinica y datos del paciente |
| P05 | Agenda | Atenciones por fecha, sede y responsable |
| P06 | Spa | Servicios y seguimiento de grooming |
| P07 | Productos | Catalogo, stock y productos de servicio |
| P08 | Ventas | Registro de venta y comprobante |
| P09 | Caja | Apertura, movimientos y cierre |
| P10 | Reservas | Creacion y cierre de reservas |
| P11 | Configuracion | Empresa, sedes y token Factiliza protegido |
| P12 | Reportes | Reporte de ventas y filtros |

## Criterios para las capturas

- No mostrar tokens, contrasenas ni datos reales.
- Mantener visible el nombre Petmor.
- Incluir fecha y version en el pie del documento.
- Usar datos de prueba generados localmente.

## Evidencia de ejecucion

El archivo `ejecutar.cmd` prepara la base, inicia la aplicacion y abre el
navegador. Las capturas finales deben reemplazar cualquier imagen heredada de
la version anterior.
