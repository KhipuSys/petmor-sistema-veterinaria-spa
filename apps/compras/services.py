from decimal import Decimal
from django.core.exceptions import ValidationError
from django.db import transaction
from apps.inventario.models import Producto
from apps.inventario.services import registrar_movimiento
from .models import Compra, DetalleCompra


@transaction.atomic
def recibir_compra(*, proveedor, comprobante, fecha, observaciones, lineas, usuario):
    if not lineas:
        raise ValidationError('Agregue al menos un producto.')
    compra = Compra.objects.create(proveedor=proveedor, comprobante=comprobante, fecha=fecha,
                                   observaciones=observaciones, usuario=usuario)
    total = Decimal('0.00')
    for linea in sorted(lineas, key=lambda item: item['producto'].pk):
        cantidad, costo = linea['cantidad'], linea['costo_unitario']
        if cantidad <= 0 or not costo.is_finite() or costo < 0:
            raise ValidationError('Cantidad o costo no válido.')
        producto = Producto.objects.select_for_update().get(pk=linea['producto'].pk)
        subtotal = (cantidad * costo).quantize(Decimal('.01'))
        total += subtotal
        if total > Decimal('9999999999.99'):
            raise ValidationError('El total excede el límite permitido.')
        DetalleCompra.objects.create(compra=compra, producto=producto, cantidad=cantidad,
                                     costo_unitario=costo, subtotal=subtotal)
        registrar_movimiento(producto=producto, tipo='ENTRADA', cantidad=cantidad,
                             motivo=f'Compra #{compra.pk}', comprobante=comprobante, usuario=usuario)
        Producto.objects.filter(pk=producto.pk).update(precio_compra=costo)
    compra.total = total
    compra.save(update_fields=['total'])
    return compra


@transaction.atomic
def anular_compra(pk, usuario):
    compra = Compra.objects.select_for_update().get(pk=pk)
    if compra.estado != 'RECIBIDA':
        raise ValidationError('Esta compra ya fue anulada.')
    for detalle in compra.detalles.select_related('producto').order_by('producto_id'):
        registrar_movimiento(producto=detalle.producto, tipo='SALIDA', cantidad=detalle.cantidad,
                             motivo=f'Anulación compra #{compra.pk}', comprobante=compra.comprobante, usuario=usuario)
    compra.estado = 'ANULADA'
    compra.save(update_fields=['estado'])
