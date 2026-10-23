from django.db import models

class Categoria(models.Model):
    nombre = models.CharField(max_length=100, unique=True)
    descripcion = models.TextField(blank=True, null=True)

    def __str__(self):
        return self.nombre


class Proveedor(models.Model):
    nombre = models.CharField(max_length=150)
    ruc = models.CharField(max_length=20, unique=True, blank=True, null=True)
    telefono = models.CharField(max_length=20, blank=True, null=True)
    email = models.EmailField(blank=True, null=True)
    direccion = models.TextField(blank=True, null=True)
    contacto = models.CharField(max_length=100, blank=True, null=True)

    def __str__(self):
        return self.nombre


class Producto(models.Model):
    area = models.CharField(max_length=15, choices=[('GENERAL', 'General / tienda'), ('VETERINARIA', 'Veterinaria'), ('SPA', 'Spa')], default='GENERAL')
    unidad = models.CharField(max_length=20, default='unidad', help_text='Unidad de stock: unidad, ml, dosis, etc. Se manejan cantidades enteras.')
    codigo = models.CharField(max_length=50, unique=True, blank=True, null=True)
    nombre = models.CharField(max_length=200)
    categoria = models.ForeignKey(Categoria, on_delete=models.SET_NULL, null=True, related_name='productos')
    proveedor = models.ForeignKey(Proveedor, on_delete=models.SET_NULL, null=True, related_name='productos')
    descripcion = models.TextField(blank=True, null=True)
    
    precio_compra = models.DecimalField(max_digits=8, decimal_places=2, default=0.00)
    precio_venta = models.DecimalField(max_digits=8, decimal_places=2, default=0.00)
    
    stock_actual = models.IntegerField(default=0)
    stock_minimo = models.IntegerField(default=5)
    
    fecha_vencimiento = models.DateField(blank=True, null=True)
    is_active = models.BooleanField(default=True)

    def __str__(self):
        return f"{self.nombre} (Stock: {self.stock_actual})"

    @property
    def necesita_reposicion(self):
        return self.stock_actual <= self.stock_minimo


class Movimiento(models.Model):
    TIPOS = (
        ('ENTRADA', 'Entrada'),
        ('SALIDA', 'Salida'),
        ('AJUSTE', 'Ajuste de Inventario'),
    )
    producto = models.ForeignKey(Producto, on_delete=models.CASCADE, related_name='movimientos')
    tipo = models.CharField(max_length=10, choices=TIPOS)
    cantidad = models.IntegerField()
    fecha = models.DateTimeField(auto_now_add=True)
    motivo = models.CharField(max_length=200)
    comprobante = models.CharField(max_length=100, blank=True, null=True)
    saldo_anterior = models.IntegerField(null=True, editable=False)
    saldo_posterior = models.IntegerField(null=True, editable=False)
    usuario = models.ForeignKey('auth.User', on_delete=models.SET_NULL, null=True, blank=True)
    
    def __str__(self):
        return f"{self.tipo} - {self.producto.nombre} ({self.cantidad})"

    def save(self, *args, **kwargs):
        from .services import guardar_movimiento
        guardar_movimiento(self)
