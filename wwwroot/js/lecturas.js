// Un Enter = una unidad. La cola conserva lecturas rápidas mientras se consulta el catálogo.
window.PetmorLecturas = {
    conectar(root, aceptar, deshacer) {
        const input = root.querySelector('[data-codigo]');
        const mensaje = root.querySelector('[data-mensaje]');
        let cola = Promise.resolve(), pendientes = 0;
        const agregar = () => {
            const original = input.value;
            if (/[^\x20-\x7e]/.test(original)) { mensaje.textContent = 'Código inválido: no se permiten controles ni Unicode.'; return; }
            const codigo = original.replace(/^ +| +$/g, '');
            if (codigo.length > 100) { mensaje.textContent = 'Máximo 100 caracteres por código. No se ha recortado la lectura.'; return; }
            input.value = ''; input.focus();
            if (!codigo) return;
            pendientes++;
            cola = cola.then(async () => {
                const response = await fetch('/Lecturas/Resolver?codigo=' + encodeURIComponent(codigo));
                if (!response.ok) {
                    let error = null;
                    try { error = await response.json(); } catch { }
                    throw new Error(error?.mensaje || 'No se pudo validar el código. Reintente la lectura.');
                }
                const p = await response.json();
                await aceptar(codigo, p);
                mensaje.textContent = (root.dataset.modo === 'asociar' ? 'Código asociado pendiente: ' : 'Lectura registrada: ') + codigo;
            }).catch(error => { mensaje.textContent = error.message; })
                .finally(() => pendientes--);
        };
        input.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); agregar(); } });
        root.querySelector('[data-agregar]').addEventListener('click', agregar);
        root.querySelector('[data-deshacer]').addEventListener('click', () => {
            cola = cola.then(() => { deshacer(); mensaje.textContent = root.dataset.modo === 'asociar' ? 'Última asociación deshecha.' : 'Última lectura deshecha.'; });
        });
        root.closest('form')?.addEventListener('submit', e => {
            if (pendientes || input.value.replace(/^ +| +$/g, '')) { e.preventDefault(); mensaje.textContent = 'Termine de registrar las lecturas antes de guardar.'; }
        });
        return { pendiente: () => pendientes > 0 || !!input.value.replace(/^ +| +$/g, '') };
    },
    formulario(root) {
        const form = root.closest('form');
        const modo = root.dataset.modo;
        const cantidad = form.querySelector(root.dataset.cantidad);
        const producto = form.querySelector(root.dataset.producto || (modo === 'operacion' ? '[name="productoId"]' : '[name="Id"]'));
        const lista = root.querySelector('[data-lista]');
        const inicial = JSON.parse(root.querySelector('[data-inicial]').textContent);
        const excepcion = root.querySelector('[data-excepcion-manual]');
        let lecturas = inicial || [];
        const render = () => {
            lista.replaceChildren();
            const grupos = new Map();
            lecturas.forEach(c => {
                const hidden = document.createElement('input'); hidden.type = 'hidden'; hidden.name = 'lecturas'; hidden.value = c; lista.append(hidden);
                grupos.set(c, (grupos.get(c) || 0) + 1);
            });
            grupos.forEach((n, c) => { const row = document.createElement('div'); row.textContent = c + ' × ' + n; lista.append(row); });
            if (modo === 'alta') form.querySelector('[name="CodigoBarras"]').value = lecturas[0] || '';
            if (excepcion && lecturas.length) excepcion.checked = false;
            if (cantidad && modo !== 'asociar') {
                cantidad.readOnly = modo === 'alta' || !excepcion?.checked;
                if (cantidad.readOnly) cantidad.value = lecturas.length;
                cantidad.dispatchEvent(new Event('input', { bubbles: true }));
            }
            if (excepcion) excepcion.disabled = lecturas.length > 0;
        };
        this.conectar(root, (codigo, p) => {
            if (excepcion?.checked) throw new Error('Desactive la excepción manual antes de escanear.');
            if (lecturas.length >= 2000) throw new Error('Máximo 2000 lecturas por detalle.');
            if (lecturas.some(x => x.toLowerCase() === codigo.toLowerCase())) throw new Error('Este código ya fue escaneado en esta operación. Cada código cuenta una sola unidad.');
            if (modo === 'alta' || modo === 'asociar' || modo === 'producto-ajuste') {
                if (p.encontrado && String(p.id) !== String(producto?.value || '0')) throw new Error('Este código pertenece a otro producto.');
            } else {
                if (!p.encontrado || !p.activo || p.servicio) throw new Error('Código inexistente o producto no disponible.');
                if (lecturas.length && String(producto.value) !== String(p.id)) throw new Error('Deshaga las lecturas antes de cambiar de producto.');
                if (![...producto.options].some(o => o.value === String(p.id))) producto.add(new Option(p.nombre, p.id));
                producto.value = p.id;
                if (window.jQuery) window.jQuery(producto).trigger('change');
            }
            lecturas.push(codigo); render();
        }, () => { lecturas.pop(); render(); });
        // Impide mezclar lecturas de un producto con la selección manual de otro.
        producto?.addEventListener('change', () => { if (modo === 'operacion' && lecturas.length) root.querySelector('[data-mensaje]').textContent = 'Las lecturas se validarán contra el producto seleccionado al guardar.'; });
        excepcion?.addEventListener('change', render);
        form.addEventListener('submit', e => {
            if (modo === 'operacion' && !excepcion?.checked && !lecturas.length) {
                e.preventDefault(); root.querySelector('[data-mensaje]').textContent = 'Registre al menos una lectura.';
            }
        });
        render();
    }
};
document.querySelectorAll('[data-lecturas-form]').forEach(root => PetmorLecturas.formulario(root));
