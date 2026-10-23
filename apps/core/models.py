from django.db import models

class Clinica(models.Model):
    nombre = models.CharField(max_length=150, default="Mi Veterinaria")
    razon_social = models.CharField(max_length=150, blank=True, null=True)
    ruc = models.CharField(max_length=20, blank=True, null=True)
    direccion = models.CharField(max_length=255, blank=True, null=True)
    telefono = models.CharField(max_length=50, blank=True, null=True)
    email = models.EmailField(blank=True, null=True)
    logo = models.ImageField(upload_to='clinica/', blank=True, null=True)
    
    # Colores/Diseño personalizados basados en el mockup
    color_primario = models.CharField(max_length=20, default="#0bb8c8", help_text="Color teal del sidebar")
    
    class Meta:
        verbose_name_plural = "Datos de la Clínica"

    def __str__(self):
        return self.nombre
