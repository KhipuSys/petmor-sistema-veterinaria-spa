from django.db import models
from apps.clientes.models import Cliente
from apps.inventario.models import Producto
from django.utils import timezone

class Factura(models.Model):
    METODOS = (
        ('EFECTIVO', 'Efectivo'),
        ('TARJETA', 'Tarjeta (POS)'),
        ('TRANSFERENCIA', 'Transferencia / Yape / Plin'),
    )
    ESTADOS = (
        ('PAGADA', 'Pagada'),
        ('ANULADA', 'Anulada'),
    )
    cliente = models.ForeignKey(Cliente, on_delete=models.SET_NULL, null=True, related_name='facturas')
    fecha = models.DateTimeField(auto_now_add=True)
    numero = models.CharField(max_length=20, unique=True)
    subtotal = models.DecimalField(max_digits=10, decimal_places=2, default=0.00)
    igv = models.DecimalField(max_digits=10, decimal_places=2, default=0.00)
    total = models.DecimalField(max_digits=10, decimal_places=2, default=0.00)
    metodo_pago = models.CharField(max_length=20, choices=METODOS, default='EFECTIVO')
    estado = models.CharField(max_length=20, choices=ESTADOS, default='PAGADA')
    cajero = models.CharField(max_length=100, blank=True, null=True)
    stock_aplicado = models.BooleanField(default=False, editable=False)
    clave_operacion = models.UUIDField(null=True, blank=True, unique=True, editable=False)

    def __str__(self):
        return f"Factura #{self.numero} - {self.cliente}"


class DetalleFactura(models.Model):
    factura = models.ForeignKey(Factura, on_delete=models.CASCADE, related_name='detalles')
    producto = models.ForeignKey(Producto, on_delete=models.SET_NULL, null=True) # Puede ser servicio o producto
    servicio = models.ForeignKey('grooming.CatalogoServicio', on_delete=models.PROTECT, null=True, blank=True)
    descripcion = models.CharField(max_length=200) # En caso producto sea null (servicios no inventariados)
    cantidad = models.IntegerField(default=1)
    precio_unitario = models.DecimalField(max_digits=10, decimal_places=2)
    subtotal = models.DecimalField(max_digits=10, decimal_places=2)

    def __str__(self):
        return f"{self.cantidad} x {self.descripcion} (Fact. {self.factura.numero})"

    def save(self, *args, **kwargs):
        self.subtotal = self.cantidad * self.precio_unitario
        super().save(*args, **kwargs)


class Caja(models.Model):
    fecha = models.DateField(default=timezone.localdate, unique=True)
    monto_inicial = models.DecimalField(max_digits=10, decimal_places=2, default=0.00)
    total_ingresos = models.DecimalField(max_digits=10, decimal_places=2, default=0.00)
    total_egresos = models.DecimalField(max_digits=10, decimal_places=2, default=0.00) # Gastos varios
    monto_final = models.DecimalField(max_digits=10, decimal_places=2, default=0.00) # Calculado: inicial + ingresos - egresos
    cerrada = models.BooleanField(default=False)
    fecha_cierre = models.DateTimeField(null=True, blank=True)
    usuario_cierre = models.CharField(max_length=100, blank=True, null=True)

    def __str__(self):
        return f"Caja del día {self.fecha} - Estado: {'Cerrada' if self.cerrada else 'Abierta'}"
