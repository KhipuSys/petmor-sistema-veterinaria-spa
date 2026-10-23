# 11. Flujo de aprobación del cambio

## Flujo propuesto

```mermaid
flowchart LR
    A[Solicitar cambio] --> B[Registrar RFC]
    B --> C[Analizar impacto y riesgos]
    C --> D[Revisar y aprobar plan]
    D --> E[Implementar en copia]
    E --> F[Ejecutar pruebas]
    F --> G{¿Pruebas aprobadas?}
    G -- No --> H[Corregir y repetir]
    H --> F
    G -- Sí --> I[Revisión técnica]
    I --> J[Aprobación del responsable]
    J --> K[Actualizar línea base]
```

## Responsables

| Etapa | Responsable |
| --- | --- |
| Solicitud | Usuario o integrante del equipo |
| Análisis | Líder técnico |
| Revisión | Equipo de desarrollo |
| Pruebas | Responsable de calidad |
| Aprobación | Docente o responsable del proyecto |
| Liberación | Líder técnico |

## Criterios de aprobación

- El cambio está descrito y justificado.
- Se conocen los archivos afectados.
- Se identificaron riesgos y reversión.
- Las pruebas automatizadas pasan.
- No se incluyen secretos ni datos reales.
- La documentación queda actualizada.

## Aplicación al RFC-001

El cambio de SQLite fue analizado, implementado en una copia, probado con migraciones, seed, mantenimiento, smoke tests y revisión de seguridad. La copia original se conservó como respaldo de la línea base anterior.
