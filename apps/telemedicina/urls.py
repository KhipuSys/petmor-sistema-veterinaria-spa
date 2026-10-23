from django.urls import path
from . import views

urlpatterns = [
    path('', views.telemedicina_lista, name='telemedicina_lista'),
    path('crear/', views.telemedicina_crear, name='telemedicina_crear'),
    path('editar/<int:pk>/', views.telemedicina_editar, name='telemedicina_editar'),
    path('estado/<int:pk>/<str:estado>/', views.telemedicina_estado, name='telemedicina_estado'),
]
