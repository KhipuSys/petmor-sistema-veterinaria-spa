from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.http import JsonResponse
from django.utils import timezone
from django.db.models import Sum, Q
from decimal import Decimal
from apps.facturacion.models import Factura, DetalleFactura, Caja
from apps.clientes.models import Cliente
from apps.inventario.models import Producto
import uuid
from django.core.exceptions import ValidationError
from django.db import IntegrityError
from apps.grooming.models import CatalogoServicio
from .services import registrar_venta, anular_venta


def get_numero_factura():
    """Genera el número de factura auto-incremental."""
    last = Factura.objects.order_by('-id').first()
    if last:
        try:
            num = int(last.numero.split('-')[-1]) + 1
        except Exception:
            num = last.id + 1
    else:
        num = 1
    return f"F-{num:06d}"


@login_required
def factura_lista(request):
    fecha_desde = request.GET.get('fecha_desde', '')
    fecha_hasta = request.GET.get('fecha_hasta', '')
    q = request.GET.get('q', '')

    facturas = Factura.objects.select_related('cliente').order_by('-fecha')

    if fecha_desde:
        facturas = facturas.filter(fecha__date__gte=fecha_desde)
    if fecha_hasta:
        facturas = facturas.filter(fecha__date__lte=fecha_hasta)
    if q:
        facturas = facturas.filter(
            Q(numero__icontains=q) | Q(cliente__nombres__icontains=q) | Q(cliente__apellidos__icontains=q)
        )

    total_ingresos = facturas.filter(estado='PAGADA').aggregate(t=Sum('total'))['t'] or 0

    return render(request, 'facturacion/lista.html', {
        'facturas': facturas,
        'total_ingresos': total_ingresos,
        'fecha_desde': fecha_desde,
        'fecha_hasta': fecha_hasta,
        'q': q,
    })


@login_required
def factura_crear(request):
    clientes = Cliente.objects.all().order_by('nombres')
    productos = Producto.objects.filter(is_active=True).order_by('nombre')

    if request.method == 'POST':
        try:
            clave = uuid.UUID(request.POST.get('clave_operacion', ''))
            cliente_id = request.POST.get('cliente')
            cliente = Cliente.objects.get(pk=int(cliente_id)) if cliente_id else None
            columnas = [request.POST.getlist(k) for k in ('descripcion[]', 'cantidad[]', 'precio[]', 'producto_id[]')]
            if len({len(c) for c in columnas}) != 1:
                raise ValidationError('Los ítems están incompletos.')
            lineas = [dict(zip(('descripcion', 'cantidad', 'precio', 'producto_id'), valores)) for valores in zip(*columnas)]
            factura = registrar_venta(cliente=cliente, metodo_pago=request.POST.get('metodo_pago'),
                                     lineas=lineas, usuario=request.user, clave=clave)
        except (ValueError, Cliente.DoesNotExist):
            messages.error(request, 'Datos no válidos. Revise el cliente y vuelva a intentar.')
        except ValidationError as exc:
            messages.error(request, '; '.join(exc.messages))
        except IntegrityError:
            messages.error(request, 'La operación ya se registró o cambió durante el envío. Revise las ventas antes de repetir.')
        else:
            messages.success(request, f'Venta {factura.numero} registrada por S/ {factura.total}.')
            return redirect('factura_detalle', pk=factura.pk)
    return render(request, 'facturacion/nueva_factura.html', {
        'clientes': clientes, 'productos': productos,
        'servicios': CatalogoServicio.objects.filter(activo=True).order_by('area', 'nombre'),
        'clave_operacion': uuid.uuid4(),
    })


@login_required
def factura_detalle(request, pk):
    factura = get_object_or_404(Factura.objects.select_related('cliente').prefetch_related('detalles__producto'), pk=pk)
    return render(request, 'facturacion/detalle_factura.html', {'factura': factura})


@login_required
def factura_anular(request, pk):
    factura = get_object_or_404(Factura, pk=pk)
    if request.method == 'POST':
        try:
            anular_venta(pk, request.user)
        except ValidationError as exc:
            messages.error(request, '; '.join(exc.messages))
        else:
            messages.warning(request, f'Venta {factura.numero} anulada; caja y stock actualizados.')
        return redirect('factura_lista')
    return render(request, 'clientes/confirmar_eliminar.html', {'objeto': factura, 'tipo': 'factura (anular)'})


@login_required
def caja_hoy(request):
    hoy = timezone.localdate()
    caja, created = Caja.objects.get_or_create(fecha=hoy, defaults={'monto_inicial': 0})
    facturas_hoy = Factura.objects.filter(fecha__date=hoy, estado='PAGADA')
    total_hoy = facturas_hoy.aggregate(t=Sum('total'))['t'] or 0
    return render(request, 'facturacion/caja.html', {
        'caja': caja, 'facturas_hoy': facturas_hoy, 'total_hoy': total_hoy, 'hoy': hoy
    })


@login_required
def caja_cerrar(request):
    if request.method == 'POST':
        hoy = timezone.localdate()
        caja = get_object_or_404(Caja, fecha=hoy)
        if not caja.cerrada:
            caja.cerrada = True
            caja.fecha_cierre = timezone.now()
            caja.usuario_cierre = request.user.get_full_name() or request.user.username
            caja.save()
            messages.success(request, 'Caja cerrada correctamente.')
        return redirect('caja_hoy')


@login_required
def api_producto_precio(request, pk):
    """Devuelve precio y nombre del producto para llenado AJAX en factura."""
    producto = get_object_or_404(Producto, pk=pk)
    return JsonResponse({
        'nombre': producto.nombre,
        'precio': float(producto.precio_venta),
        'stock': producto.stock_actual,
    })
