# 1. Descripción del proyecto

## Nombre

**Petmor: sistema de gestión para veterinaria y spa.**

## Objetivo

Centralizar la atención veterinaria, el cuidado de mascotas, los servicios de grooming y la gestión administrativa de la clínica en una sola aplicación web.

## Funcionalidades principales

- Registro de clientes y mascotas.
- Agenda de citas.
- Historias clínicas, vacunas y recetas.
- Telemedicina.
- Órdenes y catálogo de servicios de spa.
- Proveedores, compras e inventario.
- Kardex de movimientos.
- Facturación y control de caja.
- Reportes PDF y Excel.
- Usuarios, configuración y mantenimiento.

## Tecnologías

- Python 3.10+.
- Django 5.2.
- SQLite para la ejecución local.
- HTML, CSS y JavaScript.
- Bootstrap, Font Awesome y Chart.js locales.

## Arquitectura

Petmor utiliza una arquitectura monolítica basada en Django MVT. Las aplicaciones se organizan por dominio dentro de `apps/`, las reglas transaccionales se concentran en servicios y la interfaz se renderiza mediante plantillas Django.

## Usuarios beneficiados

- Administradores de la veterinaria.
- Veterinarios.
- Personal de recepción.
- Personal de grooming.
- Encargados de inventario, caja y reportes.
