// Los importes se redondean en centavos, igual que decimal/AwayFromZero en servidor.
window.PetmorImportes = Object.freeze({
    centavos: valor => Math.round(Number(valor) * 100),
    porcentaje: (importe, porcentaje) => Math.floor((Math.round(Number(importe) * 100) * porcentaje + 50) / 100) / 100,
    texto: valor => String(valor ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]))
});
