from django.db import models


class Configuracion(models.Model):
    """Modelo singleton para la configuración global del sistema."""
    nombre_empresa = models.CharField(max_length=150, default='VetSystem')
    slogan = models.CharField(max_length=200, blank=True, null=True)
    logo = models.ImageField(upload_to='configuracion/', blank=True, null=True)
    
    # Moneda
    simbolo_moneda = models.CharField(max_length=10, default='S/', help_text='Ej: S/, $, €, Bs.')
    nombre_moneda = models.CharField(max_length=50, default='Soles', blank=True)
    
    # Identificación fiscal
    ruc = models.CharField(max_length=20, blank=True, null=True, verbose_name='RUC / RIF / NIT')
    
    # Contacto
    telefono = models.CharField(max_length=30, blank=True, null=True)
    telefono2 = models.CharField(max_length=30, blank=True, null=True, verbose_name='Teléfono alternativo')
    email = models.EmailField(blank=True, null=True)
    sitio_web = models.URLField(blank=True, null=True)
    
    # Dirección
    direccion = models.CharField(max_length=255, blank=True, null=True)
    ciudad = models.CharField(max_length=100, blank=True, null=True)
    pais = models.CharField(max_length=100, default='Perú', blank=True)
    
    # Personalización visual
    color_primario = models.CharField(max_length=7, default='#0bb8c8', help_text='Color hexadecimal Ej: #0bb8c8')

    class Meta:
        verbose_name = 'Configuración'
        verbose_name_plural = 'Configuración'

    def __str__(self):
        return self.nombre_empresa

    @classmethod
    def get_config(cls):
        """Retorna la única instancia de configuración, creándola si no existe."""
        obj, _ = cls.objects.get_or_create(pk=1)
        return obj

    def save(self, *args, **kwargs):
        # Garantizar que solo exista una instancia (singleton)
        self.pk = 1
        super().save(*args, **kwargs)
