from django.db import models


class Compra(models.Model):
    proveedor = models.ForeignKey('inventario.Proveedor', on_delete=models.PROTECT, related_name='compras')
    comprobante = models.CharField(max_length=100)
    fecha = models.DateField()
    registrada = models.DateTimeField(auto_now_add=True)
    usuario = models.ForeignKey('auth.User', on_delete=models.PROTECT)
    total = models.DecimalField(max_digits=12, decimal_places=2, default=0)
    estado = models.CharField(max_length=12, default='RECIBIDA', choices=[('RECIBIDA', 'Recibida'), ('ANULADA', 'Anulada')])
    observaciones = models.TextField(blank=True)

    class Meta:
        ordering = ['-registrada']
        constraints = [models.UniqueConstraint(fields=['proveedor', 'comprobante'], name='compra_comprobante_proveedor_unico')]

    def __str__(self):
        return f'Compra {self.pk} · {self.comprobante}'


class DetalleCompra(models.Model):
    compra = models.ForeignKey(Compra, on_delete=models.CASCADE, related_name='detalles')
    producto = models.ForeignKey('inventario.Producto', on_delete=models.PROTECT)
    cantidad = models.PositiveIntegerField()
    costo_unitario = models.DecimalField(max_digits=8, decimal_places=2)
    subtotal = models.DecimalField(max_digits=12, decimal_places=2)
