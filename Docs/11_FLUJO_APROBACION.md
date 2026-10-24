# 11. Flujo de aprobacion del cambio

```mermaid
flowchart TD
    A[Solicitud RFC] --> B[Revision funcional]
    B --> C{Cumple objetivo?}
    C -- No --> D[Devolver para aclaracion]
    D --> A
    C -- Si --> E[Revision tecnica y riesgos]
    E --> F{Riesgo aceptable?}
    F -- No --> G[Definir controles o rechazar]
    G --> A
    F -- Si --> H[Aprobacion del responsable]
    H --> I[Implementacion en rama]
    I --> J[Pruebas y revision]
    J --> K{Pruebas correctas?}
    K -- No --> I
    K -- Si --> L[Merge y despliegue]
    L --> M[Verificacion posterior]
```

## Evidencias requeridas

- RFC aprobada.
- Diferencia de codigo revisada.
- Build y pruebas.
- Capturas del comportamiento.
- Registro del commit y version publicada.
