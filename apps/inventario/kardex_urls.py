from django.urls import path
from . import views

urlpatterns = [
    path('', views.movimiento_lista, name='movimiento_lista'),
    path('nuevo/', views.movimiento_crear, name='movimiento_crear'),
]
