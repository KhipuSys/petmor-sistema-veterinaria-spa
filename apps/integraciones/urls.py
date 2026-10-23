from django.urls import path
from . import controllers

urlpatterns = [
    path('', controllers.estado, name='integraciones_estado'),
    path('factiliza/documento/', controllers.documento, name='factiliza_documento'),
]
