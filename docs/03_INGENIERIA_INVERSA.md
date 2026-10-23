# 3. Pasos y diagramas de ingeniería inversa

## Pasos realizados

1. Identificar `manage.py` como punto de entrada.
2. Revisar `veterinaria/settings.py` para conocer configuración y dependencias.
3. Revisar `veterinaria/urls.py` y los `urls.py` de cada aplicación.
4. Inventariar modelos y migraciones para reconstruir entidades y relaciones.
5. Seguir los flujos de clientes, citas, historias, compras, inventario, ventas y caja.
6. Revisar servicios de negocio y formularios.
7. Relacionar vistas con plantillas y recursos estáticos.
8. Ejecutar pruebas y validar la aplicación desde una base SQLite vacía.

## Arquitectura recuperada

```mermaid
flowchart LR
    Usuario --> Plantillas
    Plantillas --> Vistas
    Vistas --> Servicios
    Servicios --> ORM
    ORM --> SQLite
    Vistas --> Reportes
    Vistas --> Factiliza
```

## Módulos identificados

```mermaid
flowchart TD
    Core[Core y Dashboard]
    Core --> Clinica[Clientes, Agenda y Médico]
    Core --> Spa[Grooming y Telemedicina]
    Core --> ERP[Compras, Inventario y Facturación]
    Core --> Admin[Usuarios, Configuración y Reportes]
```

## Flujo de una venta

```mermaid
sequenceDiagram
    Usuario->>FacturaView: Envía venta
    FacturaView->>FacturaService: Valida líneas y caja
    FacturaService->>InventarioService: Registra salida
    InventarioService->>SQLite: Actualiza stock y kardex
    FacturaService->>SQLite: Guarda factura y caja
    FacturaView-->>Usuario: Confirmación
```
