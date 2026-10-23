from django.contrib import admin
from .models import ConsultaVirtual

@admin.register(ConsultaVirtual)
class ConsultaVirtualAdmin(admin.ModelAdmin):
    list_display = ('cita', 'plataforma', 'estado_conexion')
    list_filter = ('estado_conexion', 'plataforma')
    search_fields = ('cita__mascota__nombre', 'cita__mascota__cliente__nombres')
