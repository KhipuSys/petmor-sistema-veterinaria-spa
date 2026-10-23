document.querySelectorAll('button[data-consulta-documento]').forEach(button => {
    if (button.dataset.ready) return;
    button.dataset.ready = '1';
    const status = button.closest('.col-md-6, .col-12, .mb-3, form')?.querySelector('[role="status"]');
    button.addEventListener('click', async () => {
        const form = button.closest('form');
        const documento = form.elements[button.dataset.campo].value.trim();
        const valid = button.dataset.tipo === 'ruc' ? /^[0-9]{11}$/ : button.dataset.tipo === 'dni' ? /^[0-9]{8}$/ : /^(?:[0-9]{8}|[0-9]{11})$/;
        if (!valid.test(documento)) {
            status.textContent = button.dataset.tipo === 'ruc' ? 'Ingrese un RUC de 11 dígitos.' : button.dataset.tipo === 'dni' ? 'Ingrese un DNI de 8 dígitos.' : 'Ingrese un DNI de 8 dígitos o RUC de 11 dígitos.';
            status.className = 'form-text petmor-document-status text-danger';
            return;
        }
        button.disabled = true;
        status.textContent = 'Consultando…';
        status.className = 'form-text petmor-document-status text-muted';
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 15000);
        try {
            const response = await fetch(button.dataset.url, {
                method: 'POST', credentials: 'same-origin', signal: controller.signal,
                headers: {'X-CSRFToken': form.elements.csrfmiddlewaretoken.value},
                body: new URLSearchParams({documento})
            });
            const result = await response.json();
            if (!response.ok || !result.ok) throw new Error(result.mensaje || 'No se pudo consultar.');
            if (form.elements[button.dataset.campo].value.trim() !== documento) {
                status.textContent = 'El documento cambió. Vuelva a consultar.';
                return;
            }
            const data = result.data;
            const fill = (field, value) => { if (form.elements[field] && value) form.elements[field].value = value; };
            if (button.dataset.tipo === 'ruc') fill('nombre', data.nombre);
            else if (documento.length === 11) {
                fill('nombres', data.nombres || data.nombre);
                fill('apellidos', data.apellidos || '');
            }
            else { fill('nombres', data.nombres); fill('apellidos', data.apellidos); }
            fill('direccion', data.direccion);
            status.textContent = '';
            status.className = 'form-text petmor-document-status';
        } catch (error) {
            status.textContent = error.name === 'AbortError' ? 'La consulta tardó demasiado. Puede completar los datos manualmente.' : error.message;
            status.className = 'form-text petmor-document-status text-danger';
        } finally { clearTimeout(timeout); button.disabled = false; }
    });
});
