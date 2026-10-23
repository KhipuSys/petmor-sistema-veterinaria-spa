from django.db import models
from apps.clientes.models import Mascota
from django.contrib.auth.models import User

class Cita(models.Model):
    ESTADOS = (
        ('PENDIENTE', 'Pendiente'),
        ('EN_ESPERA', 'En Espera'),
        ('EN_ATENCION', 'En Atención'),
        ('COMPLETADA', 'Completada'),
        ('CANCELADA', 'Cancelada'),
    )
    TIPOS = (
        ('CONSULTA', 'Consulta Médica'),
        ('VACUNACION', 'Vacunación'),
        ('CIRUGIA', 'Cirugía'),
        ('GROOMING', 'Baño y Corte'),
        ('CONTROL', 'Control'),
    )
    
    mascota = models.ForeignKey(Mascota, on_delete=models.CASCADE, related_name='citas')
    veterinario = models.ForeignKey(User, on_delete=models.SET_NULL, null=True, limit_choices_to={'is_staff': True})
    fecha = models.DateField()
    hora = models.TimeField()
    motivo = models.CharField(max_length=200)
    tipo = models.CharField(max_length=20, choices=TIPOS, default='CONSULTA')
    estado = models.CharField(max_length=20, choices=ESTADOS, default='PENDIENTE')
    observaciones = models.TextField(blank=True, null=True)
    fecha_creacion = models.DateTimeField(auto_now_add=True)
    is_active = models.BooleanField(default=True)

    class Meta:
        ordering = ['-fecha', '-hora']

    def __str__(self):
        return f"{self.mascota.nombre} - {self.fecha} {self.hora}"
