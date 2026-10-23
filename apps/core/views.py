from django.shortcuts import render
from django.contrib.auth.decorators import login_required
from django.utils import timezone
from django.utils.formats import date_format
from django.db.models import Sum


@login_required
def dashboard(request):
    from apps.agenda.models import Cita
    from apps.clientes.models import Mascota, Cliente
    from apps.facturacion.models import Factura
    from apps.inventario.models import Producto
    from apps.grooming.models import ServicioGrooming, ConsumoInsumo
    from apps.compras.models import Compra
    import datetime

    hoy = timezone.localdate()
    mes_inicio = hoy.replace(day=1)

    # KPIs
    citas_hoy = Cita.objects.filter(fecha=hoy).count()
    total_mascotas = Mascota.objects.count()
    mascotas_mes = Mascota.objects.filter(fecha_registro__date__gte=mes_inicio).count()
    ingresos_mes = Factura.objects.filter(
        estado='PAGADA', fecha__date__gte=mes_inicio
    ).aggregate(total=Sum('total'))['total'] or 0
    ticket_promedio = (ingresos_mes / Factura.objects.filter(
        estado='PAGADA', fecha__date__gte=mes_inicio
    ).count()) if Factura.objects.filter(
        estado='PAGADA', fecha__date__gte=mes_inicio
    ).count() > 0 else 0
    productos_activos = Producto.objects.filter(is_active=True).count()
    stock_bajo = sum(1 for p in Producto.objects.filter(is_active=True) if p.necesita_reposicion)

    # Próximas citas del día
    proximas_citas = Cita.objects.filter(
        fecha=hoy
    ).exclude(estado='COMPLETADA').exclude(estado='CANCELADA').select_related(
        'mascota__cliente', 'veterinario'
    ).order_by('hora')[:5]

    # Gráfico: citas por día últimos 7 días
    labels = []
    data_citas = []
    data_ingresos = []
    for i in range(6, -1, -1):
        dia = hoy - datetime.timedelta(days=i)
        labels.append(date_format(dia, 'D'))
        data_citas.append(Cita.objects.filter(fecha=dia).count())
        ing_dia = Factura.objects.filter(
            estado='PAGADA', fecha__date=dia
        ).aggregate(t=Sum('total'))['t'] or 0
        data_ingresos.append(float(ing_dia))

    context = {
        'spa_hoy': Cita.objects.filter(fecha=hoy, tipo='GROOMING', is_active=True).exclude(estado='CANCELADA').count(),
        'spa_en_atencion': ServicioGrooming.objects.filter(is_active=True, fecha_inicio__isnull=False, fecha_fin__isnull=True).count(),
        'spa_finalizadas': ServicioGrooming.objects.filter(is_active=True, fecha_fin__date=hoy).count(),
        'compras_mes': Compra.objects.filter(fecha__gte=mes_inicio, fecha__lte=hoy, estado='RECIBIDA').aggregate(t=Sum('total'))['t'] or 0,
        'spa_costo_mes': sum(c.costo_total for c in ConsumoInsumo.objects.select_related('movimiento').filter(movimiento__fecha__date__gte=mes_inicio, movimiento__fecha__date__lte=hoy)),
        'citas_hoy': citas_hoy,
        'total_mascotas': total_mascotas,
        'mascotas_mes': mascotas_mes,
        'ingresos_mes': ingresos_mes,
        'ticket_promedio': round(ticket_promedio, 2),
        'productos_activos': productos_activos,
        'stock_bajo': stock_bajo,
        'proximas_citas': proximas_citas,
        'labels': labels,
        'data_citas': data_citas,
        'data_ingresos': data_ingresos,
    }
    return render(request, 'dashboard/index.html', context)


@login_required
def mantenimiento_index(request):
    if not request.user.is_superuser:
        from django.contrib import messages
        from django.shortcuts import redirect
        messages.error(request, 'Acceso denegado. Solo administradores pueden ver esta sección.')
        return redirect('dashboard')

    from .maintenance import get_database_status
    return render(request, 'core/mantenimiento.html', get_database_status())


@login_required
def mantenimiento_backup(request):
    if not request.user.is_superuser:
        from django.shortcuts import redirect
        return redirect('dashboard')

    from django.http import FileResponse
    from .maintenance import create_sqlite_backup

    path = create_sqlite_backup()
    return FileResponse(path.open('rb'), as_attachment=True, filename=path.name)


@login_required
def mantenimiento_restore(request):
    from django.contrib import messages
    from django.shortcuts import redirect

    if not request.user.is_superuser:
        return redirect('dashboard')

    if request.method == 'POST' and request.FILES.get('backup_file'):
        archivo = request.FILES['backup_file']
        if not archivo.name.lower().endswith('.sqlite3'):
            messages.error(request, 'El archivo debe tener extension .sqlite3.')
            return redirect('mantenimiento_index')

        from .maintenance import restore_sqlite_backup
        try:
            restore_sqlite_backup(archivo)
            messages.success(request, 'Base de datos restaurada correctamente.')
        except ValueError as error:
            messages.error(request, str(error))

    return redirect('mantenimiento_index')


@login_required
def mantenimiento_reset(request):
    from django.contrib import messages
    from django.shortcuts import redirect
    
    if not request.user.is_superuser:
        return redirect('dashboard')

    if request.method == 'POST' and request.POST.get('confirm_reset') == 'RESET_SISTEMA_VET':
        from .maintenance import reset_business_data
        try:
            reset_business_data()
            messages.success(request, 'Sistema reseteado correctamente. Datos de negocio eliminados. (Usuarios conservados)')
        except Exception:
            messages.error(request, 'No se pudo reiniciar el sistema. No se aplicaron cambios parciales.')

    else:
        messages.error(request, 'Confirmación incorrecta para resetear el sistema.')

    return redirect('mantenimiento_index')

