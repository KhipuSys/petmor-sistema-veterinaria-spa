from django.urls import path
from apps.inventario import views

urlpatterns = [
    # Productos
    path('', views.producto_lista, name='producto_lista'),
    path('nuevo/', views.producto_crear, name='producto_crear'),
    path('<int:pk>/editar/', views.producto_editar, name='producto_editar'),
    path('<int:pk>/estado/', views.producto_estado, name='producto_estado'),
    # Proveedores
    path('proveedores/', views.proveedor_lista),
    path('proveedores/nuevo/', views.proveedor_crear),
    path('proveedores/<int:pk>/editar/', views.proveedor_editar),
    # Movimientos
    path('movimientos/', views.movimiento_lista),
    path('movimientos/nuevo/', views.movimiento_crear),
    # Categorias
    path('categorias/', views.categoria_lista, name='categoria_lista'),
    path('categorias/nueva/', views.categoria_crear, name='categoria_crear'),
]
