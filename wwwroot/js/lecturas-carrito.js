window.conectarLecturasCarrito = function (lista, render, compra, bloqueado, cambioProducto) {
    const historial = [];
    return PetmorLecturas.conectar(document.getElementById('lecturasCarrito'), (codigo, p) => {
        if (bloqueado()) throw new Error('Primero confirme el resultado de la operación pendiente.');
        if (!p.encontrado || !p.activo || p.servicio || (!compra && (p.interno || p.vencido))) throw new Error('Código no disponible para esta operación.');
        const items = lista();
        if (!compra && items.filter(x => String(x.ProductoId) === String(p.id)).reduce((n, x) => n + x.Cantidad, 0) >= p.stock) throw new Error('Stock insuficiente.');
        let item = items.find(x => String(x.ProductoId) === String(p.id) && x.Lecturas);
        if (item?.Lecturas.length >= 2000) throw new Error('Máximo 2000 lecturas por detalle.');
        if (items.some(x => x.Lecturas?.some(c => c.toLowerCase() === codigo.toLowerCase()))) throw new Error('Este código ya fue escaneado en esta operación. Cada código cuenta una sola unidad.');
        if (!item) {
            if (items.length >= 200) throw new Error('Máximo 200 líneas por operación.');
            item = { ProductoId: p.id, Nombre: p.nombre, Precio: compra ? p.costo : p.precio, Cantidad: 0, Total: 0, Lecturas: [], StockOriginal: p.stock };
            items.push(item);
        }
        item.Lecturas.push(codigo); item.Cantidad = item.Lecturas.length; item.Total = item.Cantidad * item.Precio;
        historial.push(item); render();
        if (typeof cambioProducto === 'function') cambioProducto(p, item);
    }, () => {
        if (bloqueado()) return;
        const item = historial.pop(), items = lista();
        if (!item || !items.includes(item)) return;
        item.Lecturas.pop(); item.Cantidad = item.Lecturas.length; item.Total = item.Cantidad * item.Precio;
        const producto = { id: item.ProductoId, nombre: item.Nombre, precio: item.Precio, costo: item.Precio, stock: item.StockOriginal };
        if (!item.Cantidad) items.splice(items.indexOf(item), 1);
        render();
        if (typeof cambioProducto === 'function') cambioProducto(producto, item);
    });
};
