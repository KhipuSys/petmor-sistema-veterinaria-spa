from django.db import models
from apps.agenda.models import Cita
from django.contrib.auth.models import User
from django.core.validators import MinValueValidator


class CatalogoServicio(models.Model):
    nombre = models.CharField(max_length=120, unique=True)
    area = models.CharField(max_length=15, choices=[('SPA', 'Spa y peluquería'), ('VETERINARIA', 'Veterinaria')], default='SPA')
    descripcion = models.TextField(blank=True)
    precio = models.DecimalField(max_digits=8, decimal_places=2, validators=[MinValueValidator(0)])
    duracion_minutos = models.PositiveIntegerField(default=60, validators=[MinValueValidator(1)])
    activo = models.BooleanField(default=True)

    def __str__(self):
        return self.nombre

class ServicioGrooming(models.Model):
    servicio = models.ForeignKey(CatalogoServicio, on_delete=models.PROTECT, null=True, blank=True)
    cita = models.OneToOneField(Cita, on_delete=models.CASCADE, related_name='grooming')
    peluquero = models.ForeignKey(User, on_delete=models.SET_NULL, null=True, limit_choices_to={'is_staff': True})
    
    # Checklists de servicios realizados
    bano = models.BooleanField(default=False, verbose_name="Baño")
    corte_pelo = models.BooleanField(default=False, verbose_name="Corte de Pelo")
    corte_unas = models.BooleanField(default=False, verbose_name="Corte de Uñas")
    limpieza_oidos = models.BooleanField(default=False, verbose_name="Limpieza de Oídos")
    glandulas = models.BooleanField(default=False, verbose_name="Cepillado Glándulas Anales")
    perfume = models.BooleanField(default=False, verbose_name="Perfume/Lazo")
    
    observaciones = models.TextField(blank=True, null=True, help_text="Ej: Presencia de pulgas, alergias de piel")
    fecha_inicio = models.DateTimeField(null=True, blank=True)
    fecha_fin = models.DateTimeField(null=True, blank=True)
    is_active = models.BooleanField(default=True)

    def __str__(self):
        return f"Servicio Grooming - {self.cita.mascota.nombre}"


class ConsumoInsumo(models.Model):
    orden = models.ForeignKey(ServicioGrooming, on_delete=models.PROTECT, related_name='consumos')
    movimiento = models.OneToOneField('inventario.Movimiento', on_delete=models.PROTECT)
    costo_unitario = models.DecimalField(max_digits=8, decimal_places=2)
    clave = models.UUIDField(unique=True)

    @property
    def costo_total(self):
        return self.costo_unitario * self.movimiento.cantidad
