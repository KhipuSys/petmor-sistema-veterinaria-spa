from django.db import models
from apps.clientes.models import Mascota
from apps.agenda.models import Cita
from apps.inventario.models import Producto
from django.contrib.auth.models import User

class HistoriaClinica(models.Model):
    mascota = models.ForeignKey(Mascota, on_delete=models.CASCADE, related_name='historias_clinicas')
    cita = models.OneToOneField(Cita, on_delete=models.SET_NULL, null=True, blank=True)
    veterinario = models.ForeignKey(User, on_delete=models.SET_NULL, null=True)
    fecha = models.DateTimeField(auto_now_add=True)
    motivo_consulta = models.TextField()
    anamnesis = models.TextField(help_text="Antecedentes y sintomatología reportada")
    
    # Examen Físico
    peso = models.DecimalField(max_digits=5, decimal_places=2, null=True, blank=True)
    temperatura = models.DecimalField(max_digits=4, decimal_places=1, null=True, blank=True)
    frecuencia_cardiaca = models.IntegerField(null=True, blank=True)
    frecuencia_respiratoria = models.IntegerField(null=True, blank=True)
    mucosas = models.CharField(max_length=50, null=True, blank=True)
    tiempo_llenado_capilar = models.CharField(max_length=50, null=True, blank=True)
    
    diagnostico = models.TextField()
    tratamiento = models.TextField()
    observaciones = models.TextField(blank=True, null=True)
    proxima_cita = models.DateField(null=True, blank=True)

    def __str__(self):
        return f"Historia {self.id} - {self.mascota.nombre} - {self.fecha.date()}"


class Vacuna(models.Model):
    mascota = models.ForeignKey(Mascota, on_delete=models.CASCADE, related_name='vacunas')
    producto = models.ForeignKey(Producto, on_delete=models.SET_NULL, null=True, limit_choices_to={'categoria__nombre__icontains': 'vacuna'})
    nombre_vacuna = models.CharField(max_length=100) # En caso no se enlace a producto
    lote = models.CharField(max_length=50, blank=True, null=True)
    fecha_aplicacion = models.DateField()
    fecha_proxima_dosis = models.DateField(null=True, blank=True)
    veterinario = models.ForeignKey(User, on_delete=models.SET_NULL, null=True)
    observaciones = models.TextField(blank=True, null=True)

    def __str__(self):
        return f"{self.nombre_vacuna} - {self.mascota.nombre}"


class Receta(models.Model):
    historia_clinica = models.OneToOneField(HistoriaClinica, on_delete=models.CASCADE, related_name='receta')
    indicaciones_generales = models.TextField(blank=True, null=True)
    fecha = models.DateTimeField(auto_now_add=True)

    def __str__(self):
        return f"Receta #{self.id} para {self.historia_clinica.mascota.nombre}"


class DetalleReceta(models.Model):
    receta = models.ForeignKey(Receta, on_delete=models.CASCADE, related_name='detalles')
    medicamento = models.CharField(max_length=100)
    dosis = models.CharField(max_length=100)
    frecuencia = models.CharField(max_length=100)
    duracion = models.CharField(max_length=100)
    cantidad = models.IntegerField(default=1)

    def __str__(self):
        return self.medicamento
