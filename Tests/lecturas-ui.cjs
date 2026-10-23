// Ejecutar con Playwright disponible en NODE_PATH; usa Edge y catálogo simulado, nunca datos reales.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const path = require('node:path');

(async () => {
    const browser = await chromium.launch({ channel: 'msedge', headless: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.route('http://lecturas.test/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname !== '/Lecturas/Resolver') return route.fulfill({ contentType: 'text/html', body: '<html><body></body></html>' });
        assert.equal(route.request().method(), 'GET');
        const codigo = url.searchParams.get('codigo');
        await new Promise(r => setTimeout(r, 30));
        return route.fulfill({ json: { encontrado: codigo.startsWith('EXISTE'), id: 7, nombre: 'Producto', activo: true,
            servicio: false, interno: false, vencido: false, precio: 10, costo: 4, stock: 20, codigo } });
    });
    try {
        await page.goto('http://lecturas.test/');
        await page.setContent(`<form><input name="CodigoBarras" readonly><input id="Stock" value="0" readonly>
            <div data-lecturas-form data-modo="alta" data-cantidad="#Stock"><input data-codigo>
            <button type="button" data-agregar>Leer</button><button type="button" data-deshacer>Deshacer</button>
            <div data-mensaje></div><div data-lista></div><script type="application/json" data-inicial>[]</script></div>
            <button type="submit" id="guardar">Guardar</button></form>`);
        await page.evaluate(() => { window.envios = 0; document.querySelector('form').addEventListener('submit', e => { e.preventDefault(); window.envios++; }); });
        await page.addScriptTag({ path: path.join(__dirname, '../wwwroot/js/lecturas.js') });
        for (const code of ['001', '001', 'ALT']) { await page.locator('[data-codigo]').fill(code); await page.locator('[data-codigo]').press('Enter'); }
        await page.waitForFunction(() => document.querySelector('#Stock').value === '3');
        assert.equal(await page.evaluate(() => window.envios), 0, 'Enter envió el formulario');
        assert.equal(await page.locator('[name="lecturas"]').count(), 3);
        assert.equal(await page.locator('#Stock').evaluate(e => e.readOnly), true);
        await page.locator('[data-codigo]').fill('EXISTE-OTRO'); await page.locator('[data-codigo]').press('Enter');
        await page.waitForFunction(() => document.querySelector('[data-mensaje]').textContent.includes('otro producto'));
        assert.equal(await page.locator('#Stock').inputValue(), '3');
        await page.locator('[data-deshacer]').click();
        await page.waitForFunction(() => document.querySelector('#Stock').value === '2');
        assert.equal(await page.locator('[name="lecturas"]').count(), 2);
        for (const codigo of ['\u00a0ABC', '\ufeffABC', '\u200bABC', 'ÁBC']) {
            await page.locator('[data-codigo]').fill(codigo); await page.locator('[data-codigo]').press('Enter');
            assert.match(await page.locator('[data-mensaje]').textContent(), /Unicode/);
            assert.equal(await page.locator('#Stock').inputValue(), '2');
        }
        await page.locator('[data-codigo]').fill('X'.repeat(101)); await page.locator('[data-codigo]').press('Enter');
        assert.match(await page.locator('[data-mensaje]').textContent(), /No se ha recortado/);
        assert.equal(await page.locator('#Stock').inputValue(), '2');
        console.log('PASS UI: Enter no envía, repeticiones cuentan, alternativos, conflicto rechazado, readonly y deshacer.');

        await page.setContent(`<div id="lecturasCarrito"><input data-codigo><button data-agregar>Leer</button><button data-deshacer>Deshacer</button><div data-mensaje></div></div>`);
        await page.addScriptTag({ path: path.join(__dirname, '../wwwroot/js/lecturas-carrito.js') });
        await page.evaluate(() => { window.items = []; window.lector = conectarLecturasCarrito(() => window.items, () => {}, false, () => false); });
        for (const code of ['EXISTE-1', 'EXISTE-ALT', 'EXISTE-1']) { await page.locator('[data-codigo]').fill(code); await page.locator('[data-codigo]').press('Enter'); }
        await page.waitForFunction(() => window.items[0]?.Cantidad === 3 && !window.lector.pendiente());
        assert.deepEqual(await page.evaluate(() => [window.items.length, window.items[0].Total, window.items[0].Lecturas.length]), [1, 30, 3]);
        await page.locator('[data-deshacer]').click();
        await page.waitForFunction(() => window.items[0].Cantidad === 2);
        await page.evaluate(() => { window.items = []; }); // cancelar borrador
        await page.locator('[data-deshacer]').click();
        assert.equal(await page.evaluate(() => window.items.length), 0);
        assert.deepEqual(errors, []);
        console.log('PASS UI: cola de escaneos, carrito agrupa alternativos, cantidad/precio, deshacer y cancelación sin escrituras.');
        await page.setContent(`<form><select name="productoId"><option value="7">Producto</option></select><input name="cantidad" value="5" min="1" type="number">
            <div data-lecturas-form data-modo="operacion" data-cantidad="[name='cantidad']"><input data-codigo><button type="button" data-agregar>Leer</button><button type="button" data-deshacer>Deshacer</button><input type="checkbox" data-excepcion-manual><div data-mensaje></div><div data-lista></div><script type="application/json" data-inicial>[]</script></div></form>`);
        await page.evaluate(() => PetmorLecturas.formulario(document.querySelector('[data-lecturas-form]')));
        assert.equal(await page.locator('[name="cantidad"]').inputValue(), '0');
        assert.equal(await page.locator('[name="cantidad"]').evaluate(e => e.readOnly), true);
        await page.locator('[data-codigo]').fill('EXISTE-ALT'); await page.locator('[data-codigo]').press('Enter');
        await page.waitForFunction(() => document.querySelector('[name="cantidad"]').value === '1');
        assert.equal(await page.locator('[data-excepcion-manual]').isDisabled(), true);
        await page.locator('[data-deshacer]').click();
        await page.waitForFunction(() => document.querySelector('[name="cantidad"]').value === '0');
        assert.equal(await page.locator('[name="cantidad"]').evaluate(e => e.readOnly), true);
        assert.equal(await page.evaluate(() => document.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }))), false);
        await page.locator('[data-excepcion-manual]').check();
        assert.equal(await page.locator('[name="cantidad"]').evaluate(e => e.readOnly), false);
        await page.locator('[data-codigo]').fill('EXISTE-ALT'); await page.locator('[data-codigo]').press('Enter');
        await page.waitForFunction(() => document.querySelector('[data-mensaje]').textContent.includes('Desactive la excepción'));
        assert.equal(await page.locator('[name="lecturas"]').count(), 0);
        await page.locator('[data-excepcion-manual]').uncheck();
        assert.equal(await page.locator('[name="cantidad"]').evaluate(e => e.readOnly), true);
        assert.deepEqual(errors, []);
        console.log('PASS UI: deshacer todas las lecturas no introduce una unidad manual por defecto.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
