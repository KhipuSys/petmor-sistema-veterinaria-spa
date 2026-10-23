from django.urls import path
from apps.medico import views

urlpatterns = [
    path('', views.historia_lista, name='historia_lista'),
    path('nueva/', views.historia_crear, name='historia_crear'),
    path('nueva/cita/<int:cita_pk>/', views.historia_crear, name='historia_crear_desde_cita'),
    path('<int:pk>/', views.historia_detalle, name='historia_detalle'),
    path('<int:pk>/editar/', views.historia_editar, name='historia_editar'),
    # Vacunas
    path('vacunas/', views.vacuna_lista, name='vacuna_lista'),
    path('vacunas/nueva/', views.vacuna_crear, name='vacuna_crear'),
    path('vacunas/nueva/mascota/<int:mascota_pk>/', views.vacuna_crear, name='vacuna_crear_mascota'),
    path('vacunas/<int:pk>/editar/', views.vacuna_editar, name='vacuna_editar'),
]
