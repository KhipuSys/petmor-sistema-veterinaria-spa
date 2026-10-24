# 2. Diagnostico del software inicial

## Situacion inicial

El repositorio contenia una implementacion Django y una implementacion ASP.NET
con nombres heredados de un minimarket. La interfaz, la documentacion y parte
de la configuracion no representaban de forma consistente a Petmor.

## Hallazgos

| Area | Hallazgo | Impacto |
|---|---|---|
| Identidad | Nombres MiniMarket y AmayaSoft en namespaces, scripts y pruebas | Confusion de dominio |
| Arquitectura | Logica comercial y veterinaria mezclada en componentes grandes | Mantenimiento costoso |
| Datos | Conexion local no alineada con `Petmor_BD` | Instalacion inconsistente |
| Seguridad | Credenciales de arranque conocidas y cuentas heredadas | Riesgo operativo |
| Documentacion | PDFs y capturas de una version anterior | Evidencia desactualizada |
| Instalacion | Dependencia manual de SQL Server y configuracion local | Baja reproducibilidad |

## Diagnostico

El software tenia funcionalidad suficiente, pero necesitaba una normalizacion de
identidad, separacion de responsabilidades, configuracion reproducible y
documentacion basada en el codigo realmente ejecutado.

## Criterios de mejora

- El dominio visible y tecnico debe ser Petmor.
- La base de datos local debe ser `Petmor_BD`.
- El arranque debe ser repetible desde `ejecutar.cmd`.
- Las credenciales y tokens no deben versionarse.
- Cada refactorizacion debe conservar el comportamiento validado por pruebas.
