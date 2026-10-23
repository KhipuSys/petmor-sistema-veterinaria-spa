from django.db import models

class Cliente(models.Model):
    nombres = models.CharField(max_length=100)
    apellidos = models.CharField(max_length=100)
    dni = models.CharField(max_length=20, unique=True)
    telefono = models.CharField(max_length=20)
    email = models.EmailField(blank=True, null=True)
    direccion = models.TextField(blank=True, null=True)
    fecha_registro = models.DateTimeField(auto_now_add=True)
    is_active = models.BooleanField(default=True, verbose_name='Activo')

    def __str__(self):
        return f"{self.nombres} {self.apellidos}"

    @property
    def nombre_completo(self):
        return f"{self.nombres} {self.apellidos}"


class Mascota(models.Model):
    cliente = models.ForeignKey(Cliente, on_delete=models.CASCADE, related_name='mascotas')
    nombre = models.CharField(max_length=100)
    ESPECIES = (
        ('CANINO', 'Canino'),
        ('FELINO', 'Felino'),
        ('AVE', 'Ave'),
        ('ROEDOR', 'Roedor'),
        ('OTRO', 'Otro'),
    )
    especie = models.CharField(max_length=20, choices=ESPECIES)
    raza = models.CharField(max_length=100, blank=True, null=True)
    SEXO = (
        ('M', 'Macho'),
        ('H', 'Hembra'),
    )
    sexo = models.CharField(max_length=1, choices=SEXO)
    fecha_nacimiento = models.DateField(blank=True, null=True)
    peso_actual = models.DecimalField(max_digits=5, decimal_places=2, blank=True, null=True, help_text="Peso en Kg")
    color = models.CharField(max_length=50, blank=True, null=True)
    microchip = models.CharField(max_length=50, blank=True, null=True)
    foto = models.ImageField(upload_to='mascotas/', blank=True, null=True)
    fecha_registro = models.DateTimeField(auto_now_add=True)
    is_active = models.BooleanField(default=True, verbose_name='Activo')

    def __str__(self):
        return f"{self.nombre} ({self.cliente.nombre_completo})"
