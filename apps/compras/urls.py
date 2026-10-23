from django.urls import path
from . import controllers

urlpatterns = [
    path('', controllers.lista, name='compra_lista'),
    path('nueva/', controllers.crear, name='compra_crear'),
    path('<int:pk>/', controllers.detalle, name='compra_detalle'),
    path('<int:pk>/anular/', controllers.anular, name='compra_anular'),
]
