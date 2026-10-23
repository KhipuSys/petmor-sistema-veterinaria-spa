from django.urls import path
from apps.reportes import views

urlpatterns = [
    path('', views.reportes_index, name='reportes_index'),
    path('clientes/pdf/', views.reporte_clientes_pdf, name='reporte_clientes_pdf'),
    path('ventas/excel/', views.reporte_ventas_excel, name='reporte_ventas_excel'),
    path('inventario/pdf/', views.reporte_inventario_pdf, name='reporte_inventario_pdf'),
    path('vacunas/pdf/', views.reporte_vacunas_pdf, name='reporte_vacunas_pdf'),
]
