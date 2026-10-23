(function (window, document, $) {
    'use strict';

    const orange = '#f06800';
    const danger = '#dc2626';
    const muted = '#64748b';

    function swalOptions(options) {
        options = options || {};
        const customClass = Object.assign({ popup: 'petmor-alert-popup' }, options.customClass || {});
        return Object.assign({}, options, {
            customClass,
            confirmButtonColor: options.danger ? danger : (options.confirmButtonColor || orange),
            cancelButtonColor: options.cancelButtonColor || muted
        });
    }

    function alert(title, text, icon) {
        if (!window.Swal) return Promise.resolve();
        return window.Swal.fire(swalOptions({ title, text, icon: icon || 'info' }));
    }

    function confirm(options) {
        const settings = Object.assign({
            title: 'Confirmar operación',
            text: '¿Deseas continuar?',
            icon: 'question',
            confirmButtonText: 'Confirmar',
            cancelButtonText: 'Cancelar',
            showCancelButton: true
        }, options || {});
        if (!window.Swal) return Promise.resolve({ isConfirmed: false });
        return window.Swal.fire(swalOptions(settings));
    }

    function confirmDanger(options) {
        return confirm(Object.assign({ icon: 'warning', danger: true, confirmButtonText: 'Sí, continuar' }, options || {}));
    }

    function confirmCritical(options) {
        const phrase = options?.phrase || 'CONFIRMAR';
        return confirmDanger(Object.assign({
            input: 'text',
            inputLabel: `Escribe ${phrase} para confirmar`,
            inputPlaceholder: phrase,
            preConfirm: value => {
                if (value !== phrase) window.Swal.showValidationMessage(`Debes escribir ${phrase} exactamente.`);
                return value;
            }
        }, options || {}));
    }

    function select2Language(kind) {
        const noun = kind === 'cliente' ? 'clientes' : kind === 'proveedor' ? 'proveedores' : 'productos';
        return {
            inputTooShort: () => kind === 'producto' ? 'Escribe o escanea para buscar productos.' : `Escribe para buscar ${noun}.`,
            noResults: () => `No se encontraron ${noun}.`,
            searching: () => 'Buscando...',
            errorLoading: () => 'No se pudieron cargar los resultados.'
        };
    }

    function initSelect(element, options) {
        if (!$ || !$.fn.select2 || !element) return null;
        const $element = $(element);
        if ($element.data('select2')) $element.select2('destroy');
        const kind = options?.kind || element.dataset.petmorSelect || 'producto';
        return $element.select2(Object.assign({
            theme: 'bootstrap-5',
            width: '100%',
            minimumInputLength: Number(element.dataset.minInputLength ?? options?.minimumInputLength ?? 1),
            placeholder: options?.placeholder || element.dataset.placeholder || (kind === 'cliente' ? 'Buscar cliente...' : kind === 'proveedor' ? 'Buscar proveedor...' : 'Buscar por nombre o código de barras...'),
            language: select2Language(kind)
        }, options || {}));
    }

    function exportButtons(title, columns) {
        const safeTitle = title || document.title || 'Reporte Petmor';
        return [
            { extend: 'excelHtml5', className: 'pm-export-btn', text: '<i class="fas fa-file-excel"></i>Excel', title: safeTitle, exportOptions: { columns: columns || ':visible:not(.no-export)' } },
            { extend: 'pdfHtml5', className: 'pm-export-btn', text: '<i class="fas fa-file-pdf"></i>PDF', title: safeTitle, orientation: 'landscape', pageSize: 'LEGAL', exportOptions: { columns: columns || ':visible:not(.no-export)' } },
            { extend: 'print', className: 'pm-export-btn', text: '<i class="fas fa-print"></i>Imprimir', title: safeTitle, exportOptions: { columns: columns || ':visible:not(.no-export)' } }
        ];
    }

    function bindConfirmForms(root) {
        (root || document).querySelectorAll('form[data-petmor-confirm]:not([data-petmor-bound]), form.form-toggle:not([data-petmor-bound])').forEach(form => {
            form.dataset.petmorBound = 'true';
            form.addEventListener('submit', event => {
                if (form.dataset.petmorSubmitting === 'true') return;
                event.preventDefault();
                const type = form.dataset.petmorConfirm || 'normal';
                const title = form.dataset.title || form.dataset.confirmTitle || 'Confirmar operación';
                const text = form.dataset.text || form.dataset.confirmText || '¿Deseas continuar?';
                const phrase = form.dataset.phrase;
                const request = type === 'critical'
                    ? confirmCritical({ title, text, phrase, confirmButtonText: form.dataset.confirmButtonText || 'Confirmar' })
                    : type === 'danger'
                        ? confirmDanger({ title, text, confirmButtonText: form.dataset.confirmButtonText || 'Sí, continuar' })
                        : confirm({ title, text, confirmButtonText: form.dataset.confirmButtonText || 'Confirmar' });
                request.then(result => {
                    if (!result.isConfirmed) return;
                    if (!form.reportValidity()) return;
                    form.dataset.petmorSubmitting = 'true';
                    const button = form.querySelector('[type="submit"]');
                    if (button) button.disabled = true;
                    form.requestSubmit();
                });
            });
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        bindConfirmForms(document);
        document.querySelectorAll('[data-petmor-select]').forEach(element => initSelect(element));
        document.querySelectorAll('.export-actions:not(.pm-export-actions)').forEach(element => element.classList.add('pm-export-actions'));
        document.querySelectorAll('.export-btn:not(.pm-export-btn)').forEach(element => element.classList.add('pm-export-btn'));
    });

    if (window.Swal && !window.Swal.petmorStandardized) {
        const originalFire = window.Swal.fire.bind(window.Swal);
        window.Swal.fire = function (...args) {
            if (typeof args[0] === 'object' && args[0] !== null) {
                args[0] = swalOptions(args[0]);
            } else if (typeof args[0] === 'string') {
                args[0] = swalOptions({ title: args[0], text: args[1], icon: args[2] });
                args.splice(1);
            }
            return originalFire(...args);
        };
        window.Swal.petmorStandardized = true;
    }

    window.PetmorUI = { alert, confirm, confirmDanger, confirmCritical, initSelect, exportButtons, bindConfirmForms };
})(window, document, window.jQuery);
