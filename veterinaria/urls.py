from django.contrib import admin
from django.urls import path, include
from django.conf import settings
from django.conf.urls.static import static

urlpatterns = [
    path('admin/', admin.site.urls),
    path('', include('apps.core.urls')),
    path('', include('django.contrib.auth.urls')),
    path('clientes/', include('apps.clientes.urls')),
    path('agenda/', include('apps.agenda.urls')),
    path('medico/', include('apps.medico.urls')),
    path('inventario/', include('apps.inventario.urls')),
    path('proveedores/', include('apps.inventario.proveedores_urls')),
    path('kardex/', include('apps.inventario.kardex_urls')),
    path('compras/', include('apps.compras.urls')),
    path('integraciones/', include('apps.integraciones.urls')),
    path('facturacion/', include('apps.facturacion.urls')),
    path('grooming/', include('apps.grooming.urls')),
    path('reportes/', include('apps.reportes.urls')),
    path('telemedicina/', include('apps.telemedicina.urls')),
    path('usuarios/', include('apps.usuarios.urls')),
    path('configuracion/', include('apps.configuracion.urls')),
]

if settings.DEBUG:
    urlpatterns += static(settings.MEDIA_URL, document_root=settings.MEDIA_ROOT)
    urlpatterns += static(settings.STATIC_URL, document_root=settings.STATIC_ROOT)
