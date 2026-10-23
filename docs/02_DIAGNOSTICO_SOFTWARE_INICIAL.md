# 2. Diagnóstico del software inicial

## Situación encontrada

El proyecto original contenía una aplicación Django funcional, pero también mezclaba respaldos, datos reales, un entorno virtual no portable y un proyecto ASP.NET utilizado como referencia.

## Problemas identificados

| Problema | Consecuencia |
| --- | --- |
| MySQL era la configuración predeterminada | Instalación local compleja |
| El script local usaba SQLite, pero Mantenimiento usaba SQL de MySQL | Incompatibilidad funcional |
| Dependencia de CDN | La interfaz fallaba sin Internet |
| `venv` con rutas absolutas | No era portable a otro equipo |
| Sin seed reproducible | No existían datos demo consistentes |
| Bases, backups y tokens junto al código | Riesgo de filtrar información |
| Vistas con SQL, archivos y procesos externos | Alto acoplamiento y difícil mantenimiento |

## Diagnóstico técnico

El sistema tenía reglas de negocio valiosas, especialmente en compras, inventario, kardex, ventas y caja. El principal trabajo requerido no era cambiar el dominio, sino separar responsabilidades, sanear la entrega y hacer reproducible la ejecución local.

## Resultado del diagnóstico

Se decidió conservar Django, utilizar SQLite en la entrega local, crear datos ficticios, separar Mantenimiento en un servicio y documentar el cambio mediante una RFC.
