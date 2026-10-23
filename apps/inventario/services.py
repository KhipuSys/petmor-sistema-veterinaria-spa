from django.core.exceptions import ValidationError
from django.db import models, transaction
from .models import Producto, Movimiento


@transaction.atomic
def guardar_movimiento(movimiento):
    """Único punto de escritura de existencias. AJUSTE fija el conteo físico."""
    if movimiento.pk:
        raise ValidationError('El kardex es inmutable. Registre un movimiento de corrección.')
    producto = Producto.objects.select_for_update().get(pk=movimiento.producto_id)
    if not producto.is_active:
        raise ValidationError('El producto está desactivado.')
    cantidad = movimiento.cantidad
    if cantidad is None or cantidad < 0 or (movimiento.tipo != 'AJUSTE' and cantidad == 0):
        raise ValidationError('La cantidad debe ser positiva; un ajuste puede fijar cero.')
    anterior = producto.stock_actual
    if movimiento.tipo == 'AJUSTE':
        posterior = cantidad
    elif movimiento.tipo == 'ENTRADA':
        posterior = anterior + cantidad
    elif movimiento.tipo == 'SALIDA':
        posterior = anterior - cantidad
    else:
        raise ValidationError('Tipo de movimiento no válido.')
    if posterior < 0:
        raise ValidationError(f'Stock insuficiente de {producto.nombre}: disponible {anterior}.')
    if posterior > 2147483647:
        raise ValidationError('La cantidad excede el límite de existencias.')
    if not movimiento.motivo.strip():
        raise ValidationError('Indique el motivo del movimiento.')
    changed = Producto.objects.filter(pk=producto.pk, stock_actual=anterior).update(stock_actual=posterior)
    if changed != 1:
        raise ValidationError('El stock cambió durante la operación. Vuelva a intentarlo.')
    movimiento.saldo_anterior = anterior
    movimiento.saldo_posterior = posterior
    models.Model.save(movimiento)
    return movimiento


def registrar_movimiento(*, producto, tipo, cantidad, motivo, usuario=None, comprobante=''):
    movimiento = Movimiento(producto=producto, tipo=tipo, cantidad=cantidad,
                            motivo=motivo, usuario=usuario, comprobante=comprobante)
    movimiento.save()
    return movimiento
