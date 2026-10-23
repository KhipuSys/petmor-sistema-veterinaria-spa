# 10. Ejemplo de solicitud de cambio RFC

## RFC-001: Migración de ejecución local a SQLite

### Solicitante

Equipo de desarrollo Petmor.

### Descripción

Estandarizar la ejecución local en SQLite y eliminar la dependencia de MySQL para la entrega académica.

### Motivo

El proyecto original usaba SQLite desde el launcher, pero mantenía una configuración MySQL y un módulo de mantenimiento incompatible con SQLite. Esto impedía instalar el sistema de forma sencilla en otra computadora.

### Alcance

- Cambiar la base local a SQLite.
- Eliminar `mysqlclient`.
- Adaptar backup, restore y reset.
- Crear seed idempotente.
- Incorporar scripts de configuración e inicio.
- Servir los recursos frontend localmente.

### Impacto

La instalación local será más simple y reproducible. La configuración no está destinada a producción multiusuario.

### Riesgos

- Incompatibilidad con un respaldo antiguo.
- Duplicación de stock durante el seed.
- Restauración de un archivo inválido.
- Pérdida de datos si se mezclan bases reales con la demo.

### Plan de pruebas

- Migrar desde una base vacía.
- Ejecutar `seed_demo` dos veces.
- Probar backup y validación.
- Probar reset y regeneración.
- Ejecutar las 34 pruebas automatizadas.
- Comprobar el servidor sin CDN.

### Reversión

Conservar el proyecto original sin modificar y regenerar `Petmor_Final` desde esa línea base. Para datos locales, restaurar el último backup SQLite válido.
