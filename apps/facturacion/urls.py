from django.urls import path
from apps.facturacion import views

urlpatterns = [
    path('', views.factura_lista, name='factura_lista'),
    path('nueva/', views.factura_crear, name='factura_crear'),
    path('<int:pk>/', views.factura_detalle, name='factura_detalle'),
    path('<int:pk>/anular/', views.factura_anular, name='factura_anular'),
    # Caja
    path('caja/', views.caja_hoy, name='caja_hoy'),
    path('caja/cerrar/', views.caja_cerrar, name='caja_cerrar'),
    # AJAX
    path('api/producto/<int:pk>/', views.api_producto_precio, name='api_producto_precio'),
]
