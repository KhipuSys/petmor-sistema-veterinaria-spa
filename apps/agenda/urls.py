from django.urls import path
from apps.agenda import views

urlpatterns = [
    path('', views.agenda_lista, name='agenda_lista'),
    path('nueva/', views.cita_crear, name='cita_crear'),
    path('<int:pk>/editar/', views.cita_editar, name='cita_editar'),
    path('<int:pk>/estado_activo/', views.cita_estado, name='cita_estado_activo'),
    path('<int:pk>/estado/', views.cita_cambiar_estado, name='cita_cambiar_estado'),
]
