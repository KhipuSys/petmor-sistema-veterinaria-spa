(function ($) {
    'use strict';

    const dom = '<"petmor-table-toolbar"lBf><"petmor-table-scroll"rt><"petmor-table-footer"ip>';
    const buttonsDom = dom;
    function exportButtons(title, columns) {
        if (window.PetmorUI?.exportButtons) return window.PetmorUI.exportButtons(title, columns);
        return [
            { extend: 'excelHtml5', className: 'pm-export-btn', text: 'Excel' },
            { extend: 'pdfHtml5', className: 'pm-export-btn', text: 'PDF', orientation: 'landscape', pageSize: 'LEGAL' },
            { extend: 'print', className: 'pm-export-btn', text: 'Imprimir' }
        ];
    }
    window.PetmorTables = { dom, buttonsDom, exportButtons };
    $.extend(true, $.fn.dataTable.defaults, {
        dom,
        pagingType: 'simple',
        pageLength: 10,
        lengthMenu: [[10, 25, 50, 100, -1], [10, 25, 50, 100, 'TODOS']],
        language: {
            lengthMenu: 'Mostrar _MENU_ registros',
            search: 'Buscar:',
            info: 'Mostrando _START_–_END_ de _TOTAL_ registros',
            infoEmpty: 'Sin registros',
            infoFiltered: '(de _MAX_ en total)',
            emptyTable: 'No hay registros disponibles.',
            zeroRecords: 'No se encontraron resultados con los filtros aplicados.',
            processing: 'Cargando registros…',
            paginate: { previous: 'Anterior', next: 'Siguiente' }
        }
    });

    function markDirection(table) {
        const wrapper = table.closest('.dataTables_wrapper');
        if (!wrapper) return;
        wrapper.querySelectorAll('.dataTables_paginate .page-item').forEach(item => {
            item.classList.toggle('is-selected', !!table.dataset.pageDirection && item.classList.contains(table.dataset.pageDirection));
        });
    }

    // Capture before DataTables replaces its pagination nodes during a draw.
    function rememberDirection(event) {
        const button = event.target.closest('.dataTables_paginate .page-item');
        if (!button || button.classList.contains('disabled')) return;
        const table = button.closest('.dataTables_wrapper').querySelector('table.dataTable');
        if (!table) return;
        table.dataset.pageDirection = button.classList.contains('previous') ? 'previous' : 'next';
        markDirection(table);
    }
    document.addEventListener('click', rememberDirection, true);
    document.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') rememberDirection(event);
    }, true);

    $(document).on('init.dt draw.dt', function (event, settings) {
        if (settings) markDirection(settings.nTable);
    }).on('search.dt length.dt', function (event, settings) {
        if (!settings) return;
        delete settings.nTable.dataset.pageDirection;
        markDirection(settings.nTable);
    });

    $(function () {
        document.querySelectorAll('table[data-petmor-list]').forEach(table => {
            if ($.fn.dataTable.isDataTable(table)) return;
            const empty = table.tBodies[0]?.querySelector('tr:only-child > td[colspan]');
            const emptyText = empty?.textContent.trim();
            // DataTables generates its own empty row; colspan rows cannot be input data.
            if (empty) empty.parentElement.remove();
            $(table).DataTable({
                searching: false,
                ordering: false,
                order: [],
                ...(emptyText ? { language: { emptyTable: emptyText } } : {})
            });
        });
    });
})(jQuery);
