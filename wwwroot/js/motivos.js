document.addEventListener('click', event => {
    const button = event.target.closest('.js-ver-motivo');
    if (!button) return;
    const contenido = document.createElement('div');
    contenido.style.textAlign = 'center';
    const motivo = document.createElement('p');
    motivo.style.whiteSpace = 'pre-wrap';
    motivo.style.overflowWrap = 'anywhere';
    motivo.textContent = button.dataset.reason || 'Sin motivo registrado.';
    contenido.append(motivo);
    if (button.dataset.audit) {
        const audit = document.createElement('small');
        audit.className = 'text-muted';
        audit.textContent = button.dataset.audit;
        contenido.append(audit);
    }
    Swal.fire({ titleText: button.dataset.title || 'Motivo', html: contenido, icon: 'info', confirmButtonText: 'Cerrar', confirmButtonColor: '#f06800' });
});
