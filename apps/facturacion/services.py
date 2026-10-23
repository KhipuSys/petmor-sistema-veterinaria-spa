from decimal import Decimal, InvalidOperation
from django.core.exceptions import ValidationError
from django.db import transaction
from django.utils import timezone
from apps.inventario.models import Producto
from apps.inventario.services import registrar_movimiento
from apps.grooming.models import CatalogoServicio
from .models import Factura, DetalleFactura, Caja


@transaction.atomic
def registrar_venta(*, cliente, metodo_pago, lineas, usuario, clave):
    existente = Factura.objects.filter(clave_operacion=clave).first()
    if existente:
        return existente
    if metodo_pago not in dict(Factura.METODOS):
        raise ValidationError('Método de pago no válido.')
    if not lineas or len(lineas) > 100:
        raise ValidationError('Ingrese entre 1 y 100 ítems.')
    caja, _ = Caja.objects.get_or_create(fecha=timezone.localdate())
    caja = Caja.objects.select_for_update().get(pk=caja.pk)
    if caja.cerrada:
        raise ValidationError('La caja de hoy está cerrada. No se puede registrar la venta.')
    ultima = Factura.objects.order_by('-pk').first()
    numero = (ultima.pk if ultima else 0) + 1
    while Factura.objects.filter(numero=f'F-{numero:06d}').exists():
        numero += 1
    factura = Factura.objects.create(cliente=cliente, numero=f'F-{numero:06d}', metodo_pago=metodo_pago,
                                    cajero=usuario.get_full_name() or usuario.username, clave_operacion=clave,
                                    stock_aplicado=True)
    subtotal = Decimal('0.00')
    for linea in lineas:
        try:
            cantidad = int(linea['cantidad'])
            precio = Decimal(linea['precio'])
        except (ValueError, TypeError, InvalidOperation):
            raise ValidationError('Cantidad o precio no válido.')
        if cantidad <= 0 or cantidad > 1000000 or not precio.is_finite() or precio < 0 or precio > Decimal('99999999.99'):
            raise ValidationError('Cantidad o precio fuera de rango.')
        if precio != precio.quantize(Decimal('.01')):
            raise ValidationError('El precio admite como máximo dos decimales.')
        descripcion = linea['descripcion'].strip()
        if not descripcion or len(descripcion) > 200:
            raise ValidationError('La descripción es obligatoria (máximo 200 caracteres).')
        producto = servicio = None
        seleccion = linea.get('producto_id', '')
        try:
            if seleccion.startswith('s:'):
                servicio = CatalogoServicio.objects.get(pk=int(seleccion[2:]), activo=True)
                descripcion = servicio.nombre
            elif seleccion:
                producto = Producto.objects.get(pk=int(seleccion), is_active=True)
                descripcion = producto.nombre
        except (ValueError, Producto.DoesNotExist, CatalogoServicio.DoesNotExist):
            raise ValidationError('El producto o servicio seleccionado no está disponible.')
        subtotal += cantidad * precio
        if subtotal * Decimal('1.18') > Decimal('99999999.99'):
            raise ValidationError('El total supera el límite permitido.')
        DetalleFactura.objects.create(factura=factura, producto=producto, servicio=servicio,
                                     descripcion=descripcion, cantidad=cantidad, precio_unitario=precio)
        if producto:
            registrar_movimiento(producto=producto, tipo='SALIDA', cantidad=cantidad,
                                 motivo=f'Venta {factura.numero}', comprobante=factura.numero, usuario=usuario)
    factura.subtotal = subtotal
    factura.igv = (subtotal * Decimal('.18')).quantize(Decimal('.01'))
    factura.total = subtotal + factura.igv
    factura.save(update_fields=['subtotal', 'igv', 'total'])
    caja.total_ingresos += factura.total
    caja.monto_final = caja.monto_inicial + caja.total_ingresos - caja.total_egresos
    caja.save(update_fields=['total_ingresos', 'monto_final'])
    return factura


@transaction.atomic
def anular_venta(pk, usuario):
    factura = Factura.objects.select_for_update().get(pk=pk)
    if factura.estado == 'ANULADA':
        raise ValidationError('La venta ya está anulada.')
    fecha = timezone.localdate(factura.fecha)
    caja = Caja.objects.select_for_update().filter(fecha=fecha).first()
    if caja and caja.cerrada:
        raise ValidationError('La caja de esta venta está cerrada; requiere un proceso de devolución contable.')
    if factura.stock_aplicado:
        for d in factura.detalles.select_related('producto').order_by('producto_id'):
            if d.producto:
                registrar_movimiento(producto=d.producto, tipo='ENTRADA', cantidad=d.cantidad,
                                     motivo=f'Anulación venta {factura.numero}', comprobante=factura.numero, usuario=usuario)
    if caja:
        caja.total_ingresos = max(Decimal('0'), caja.total_ingresos - factura.total)
        caja.monto_final = caja.monto_inicial + caja.total_ingresos - caja.total_egresos
        caja.save(update_fields=['total_ingresos', 'monto_final'])
    factura.estado = 'ANULADA'
    factura.save(update_fields=['estado'])
