from django.db import transaction
from django.core.exceptions import ValidationError
from apps.inventario.services import registrar_movimiento
from apps.inventario.models import Producto
from .models import ConsumoInsumo, ServicioGrooming


@transaction.atomic
def consumir_insumo(*, orden, producto, cantidad, clave, usuario):
    orden = ServicioGrooming.objects.select_for_update().get(pk=orden.pk)
    if ConsumoInsumo.objects.filter(clave=clave).exists():
        raise ValidationError('Este consumo ya fue registrado. Recargue la página.')
    if not orden.is_active:
        raise ValidationError('La orden está desactivada.')
    producto = Producto.objects.select_for_update().get(pk=producto.pk)
    movimiento = registrar_movimiento(producto=producto, tipo='SALIDA', cantidad=cantidad,
                                     motivo=f'Consumo spa: orden #{orden.pk}', usuario=usuario)
    return ConsumoInsumo.objects.create(orden=orden, movimiento=movimiento,
                                       costo_unitario=producto.precio_compra, clave=clave)
