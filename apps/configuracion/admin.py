from django.contrib import admin
from apps.configuracion.models import Configuracion


@admin.register(Configuracion)
class ConfiguracionAdmin(admin.ModelAdmin):
    def has_add_permission(self, request):
        # Singleton: solo crear si no existe
        return not Configuracion.objects.exists()
