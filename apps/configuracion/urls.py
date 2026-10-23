from django.urls import path
from apps.configuracion import views

urlpatterns = [
    path('', views.configuracion_view, name='configuracion'),
]
