# 10. Ejemplo de solicitud de cambio RFC

## RFC-PETMOR-001: Integracion de consulta documentaria

**Solicitante:** Administracion Petmor  
**Prioridad:** Alta  
**Estado:** Aprobado para desarrollo

### Descripcion

Permitir consultar DNI y RUC desde el formulario de clientes mediante Factiliza.

### Justificacion

Reduce digitacion manual, mejora la calidad de datos y acelera la atencion.

### Alcance

- Configurar token desde Administracion.
- Guardarlo cifrado en la configuracion.
- Consultar Factiliza desde un servicio aislado.
- Mostrar resultado antes de guardar el cliente.

### Riesgos y controles

- Token expuesto: usar User Secrets y cifrado.
- Caida externa: mostrar error controlado.
- Datos incorrectos: permitir revision manual.

### Criterios de aceptacion

- Consulta exitosa de DNI/RUC.
- Token nunca visible en texto plano.
- Error externo sin perdida del formulario.
- Pruebas automatizadas y build aprobado.
