from django.urls import path
from apps.clientes import views

urlpatterns = [
    # Clientes
    path('', views.cliente_lista, name='cliente_lista'),
    path('nuevo/', views.cliente_crear, name='cliente_crear'),
    path('<int:pk>/', views.cliente_perfil, name='cliente_perfil'),
    path('<int:pk>/editar/', views.cliente_editar, name='cliente_editar'),
    path('<int:pk>/estado/', views.cliente_estado, name='cliente_estado'),
    # Mascotas
    path('<int:cliente_pk>/mascota/nueva/', views.mascota_crear, name='mascota_crear'),
    path('mascota/<int:pk>/editar/', views.mascota_editar, name='mascota_editar'),
    path('mascota/<int:pk>/estado/', views.mascota_estado, name='mascota_estado'),
]
