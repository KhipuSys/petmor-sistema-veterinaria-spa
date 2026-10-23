from django.urls import path
from . import views

urlpatterns = [
    path('', views.usuario_lista, name='usuario_lista'),
    path('crear/', views.usuario_crear, name='usuario_crear'),
    path('editar/<int:pk>/', views.usuario_editar, name='usuario_editar'),
    path('estado/<int:pk>/', views.usuario_estado, name='usuario_estado'),
]
