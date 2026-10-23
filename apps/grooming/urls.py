from django.urls import path
from apps.grooming import views
from . import controllers

urlpatterns = [
    path('servicios/', controllers.catalogo, name='servicio_catalogo'),
    path('servicios/nuevo/', controllers.servicio_form, name='servicio_crear'),
    path('servicios/<int:pk>/editar/', controllers.servicio_form, name='servicio_editar'),
    path('<int:pk>/insumos/', controllers.consumos, name='grooming_consumos'),
    path('', views.grooming_lista, name='grooming_lista'),
    path('nueva/', views.grooming_crear, name='grooming_crear'),
    path('<int:pk>/editar/', views.grooming_editar, name='grooming_editar'),
    path('<int:pk>/estado/', views.grooming_estado, name='grooming_estado'),
]
