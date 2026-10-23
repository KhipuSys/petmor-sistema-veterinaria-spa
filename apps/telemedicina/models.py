from django.db import models
from apps.agenda.models import Cita
from django.core.exceptions import ValidationError

class ConsultaVirtual(models.Model):
    PLATAFORMAS = (
        ('MEET', 'Google Meet'),
        ('ZOOM', 'Zoom'),
        ('WHATSAPP', 'WhatsApp Video'),
        ('OTRO', 'Otro / Teléfono'),
    )
    ESTADOS = (
        ('PROGRAMADA', 'Programada'),
        ('EN_CURSO', 'En Curso'),
        ('FINALIZADA', 'Finalizada'),
        ('CANCELADA', 'Cancelada'),
    )

    cita = models.OneToOneField(Cita, on_delete=models.CASCADE, related_name='consulta_virtual', limit_choices_to={'tipo': 'CONSULTA'})
    plataforma = models.CharField(max_length=20, choices=PLATAFORMAS, default='MEET')
    enlace_reunion = models.URLField(max_length=500, blank=True, null=True, help_text="URL o enlace para unirse a la teleconsulta")
    codigo_acceso = models.CharField(max_length=50, blank=True, null=True, help_text="Contraseña o PIN (Opcional)")
    estado_conexion = models.CharField(max_length=20, choices=ESTADOS, default='PROGRAMADA')
    notas_preliminares = models.TextField(blank=True, null=True, help_text="Síntomas reportados antes de entrar a sesión")

    def __str__(self):
        return f"Teleconsulta: {self.cita.mascota.nombre} - {self.cita.fecha} {self.cita.hora}"

    def clean(self):
        # Asegurarse de que si el enlace no esta vacio, y es whatsapp, sea un numero valido quizas, pero por ahora solo URLField ya valida
        pass
    
    class Meta:
        verbose_name = 'Consulta Virtual'
        verbose_name_plural = 'Consultas Virtuales'
