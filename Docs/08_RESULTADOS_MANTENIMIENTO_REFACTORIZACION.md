# 8. Resultados del mantenimiento y refactorizacion

## Resultados funcionales

- La aplicacion se identifica como Petmor en codigo, interfaz y documentacion.
- La base local usada es `Petmor_BD`.
- El arranque se automatiza mediante `ejecutar.cmd`.
- Se conserva el flujo de autenticacion con roles.
- Se mantienen veterinaria, spa, inventario, ventas, caja y reportes.

## Resultados de calidad

- Build de `Petmor.Web.csproj`: correcto.
- Pruebas de `Tests/Comercial.IntegrationTests.csproj`: ejecutadas correctamente.
- Login administrativo verificado con la base `Petmor_BD`.
- Referencias heredadas de MiniMarket y AmayaSoft eliminadas de archivos versionados.

## Riesgo pendiente

El build reporta una advertencia de seguridad alta en `System.Security.Cryptography.Xml`
9.0.20. Debe actualizarse en una tarea posterior validando compatibilidad.
