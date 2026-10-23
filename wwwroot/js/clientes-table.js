$(function () {
    'use strict';
    const element = document.getElementById('tablaClientes');
    const form = document.getElementById('clientesFiltros');
    const panel = element.closest('.clientes-panel');
    const shell = panel.querySelector('[data-clientes-table-shell]');
    const escape = value => $('<span>').text(value ?? '').html();
    let filtros = { q: form.elements.q.value, estado: form.elements.estado.value };
    const actions = document.getElementById('clienteAcciones');
    let currentDraw = 0;

    function renderActions(row) {
        const container = document.createElement('div');
        container.append(actions.content.cloneNode(true));
        container.querySelectorAll('[href], [action]').forEach(node => {
            const attr = node.hasAttribute('href') ? 'href' : 'action';
            node.setAttribute(attr, node.getAttribute(attr).replace('__id__', String(Number(row.id))));
        });
        const button = container.querySelector('[data-toggle-estado]');
        if (button) {
            const label = row.estado ? 'Desactivar cliente' : 'Activar cliente';
            button.title = label;
            button.setAttribute('aria-label', label);
            button.classList.add(row.estado ? 'btn-action-danger' : 'btn-action-success');
            button.firstElementChild.className = 'fas fa-power-off';
            button.form.dataset.activo = String(row.estado);
        }
        return container.innerHTML;
    }

    // Keep the server-rendered list as a no-JavaScript fallback; DataTables owns paging after this point.
    element.tBodies[0].innerHTML = '';
    const table = $(element).DataTable({
        dom: PetmorTables.dom,
        serverSide: true,
        processing: false,
        searching: false,
        displayStart: Number(element.dataset.start) || 0,
        order: [[0, 'asc']],
        initComplete: function () {
            shell.style.visibility = '';
            shell.style.minHeight = '';
            panel.classList.remove('clientes-panel--hydrating');
        },
        ajax: function (data, callback) {
            currentDraw = data.draw;
            $.ajax({
                url: element.dataset.url,
                dataType: 'json',
                data: { draw: data.draw, start: data.start, length: data.length, ...filtros,
                    columna: data.order[0]?.column ?? 0, descendente: data.order[0]?.dir === 'desc' }
            }).done(result => {
                if (data.draw !== currentDraw) return;
                callback(result);
            }).fail(() => {
                if (data.draw !== currentDraw) return;
                PetmorUI.alert('No se pudieron cargar los clientes', 'Vuelve a pulsar Filtrar para reintentar.', 'error');
                shell.style.visibility = '';
                shell.style.minHeight = '';
                panel.classList.remove('clientes-panel--hydrating');
                callback({ draw: data.draw, recordsTotal: 0, recordsFiltered: 0, data: [] });
            });
        },
        columns: [
            { data: 'nombre', render: (value, type) => type !== 'display' ? value : `<div class="cliente-cell"><div class="cliente-avatar">${escape(value?.trim().charAt(0).toUpperCase() || '?')}</div><div><strong>${escape(value)}</strong><span>Propietario registrado</span></div></div>` },
            { data: 'documento', render: (value, type) => type !== 'display' ? value : `<div class="documento-cell"><div class="documento-icon"><i class="far fa-id-card"></i></div><div><strong>${value?.length === 11 ? 'RUC' : 'DNI'}</strong><span>${escape(value)}</span></div></div>` },
            { data: 'telefono', render: (value, type, row) => type !== 'display' ? value : `<div class="contacto-cell"><span><i class="fas fa-phone me-2"></i>${escape(value || 'Sin teléfono')}</span><span><i class="far fa-envelope me-2"></i>${escape(row.correo || 'Sin correo')}</span></div>` },
            { data: 'estado', render: (value, type) => type !== 'display' ? value : `<span class="estado-badge ${value ? 'estado-activo' : 'estado-inactivo'}"><i class="fas fa-circle"></i>${value ? 'Activo' : 'Inactivo'}</span>` },
            { data: null, orderable: false, searchable: false, render: (value, type, row) => type === 'display' ? renderActions(row) : '' }
        ],
        createdRow: (row, data) => row.classList.toggle('cliente-inactivo', !data.estado)
    });
    document.querySelector('.clientes-fallback').remove();
    form.addEventListener('submit', function (event) {
        event.preventDefault();
        filtros = { q: form.elements.q.value.trim(), estado: form.elements.estado.value };
        delete element.dataset.pageDirection;
        table.ajax.reload();
    });
    $(element).on('submit', '.js-cliente-estado', function (event) {
        if (this.dataset.activo !== 'true' || this.dataset.confirmado === 'true') return;
        event.preventDefault();
        PetmorUI.confirmDanger({ title: 'Desactivar cliente', text: '¿Desactivar cliente?', confirmButtonText: 'Sí, desactivar' }).then(result => {
            if (!result.isConfirmed) return;
            this.dataset.confirmado = 'true';
            this.requestSubmit();
        });
    });
});
