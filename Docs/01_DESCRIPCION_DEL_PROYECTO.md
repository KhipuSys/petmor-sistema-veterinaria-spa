# 1. Descripcion del proyecto

## Nombre

Petmor Sistema Veterinaria Spa.

## Objetivo

Centralizar la operacion de una veterinaria y spa para mascotas: clientes,
mascotas, atenciones, servicios de spa, productos, inventario, compras, ventas,
caja, reservas, reportes y configuracion.

## Alcance funcional

- Registro de clientes y mascotas.
- Historia clinica, preventivos y archivos clinicos.
- Agenda de atenciones veterinarias y de spa.
- Catalogo de servicios y productos.
- Inventario por sede y movimientos de kardex.
- Compras, ventas, reservas y caja.
- Usuarios, roles y permisos.
- Consultas documentarias mediante Factiliza.
- Reportes operativos y comerciales.

## Alcance tecnico

La solucion es una aplicacion web ASP.NET Core sobre .NET 9, Entity Framework
Core e Identity. Usa SQL Server Express y la base `Petmor_BD`. La interfaz se
construye con Razor Views, CSS y JavaScript. La ejecucion local se automatiza
con `ejecutar.cmd`.

## Beneficiarios

Administradores, recepcionistas, veterinarios y personal de grooming que
requieren una fuente unica y trazable para la operacion diaria.
