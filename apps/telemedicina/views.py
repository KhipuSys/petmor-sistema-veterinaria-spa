from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.utils import timezone
from apps.telemedicina.models import ConsultaVirtual
from apps.telemedicina.forms import ConsultaVirtualForm
from apps.agenda.models import Cita


@login_required
def telemedicina_lista(request):
    hoy = timezone.localdate()
    # Teleconsultas programadas (vistas prioritariamente por fecha de la cita)
    consultas = ConsultaVirtual.objects.select_related('cita__mascota__cliente').order_by('estado_conexion', 'cita__fecha')
    
    # Citas tipo consulta que aÃºn no tienen teleconsulta asociada (posibles futuras teleconsultas)
    citas_sin_telemedicina = Cita.objects.filter(tipo='CONSULTA', consulta_virtual__isnull=True, fecha__gte=hoy).order_by('fecha', 'hora')
    
    return render(request, 'telemedicina/lista.html', {
        'consultas': consultas,
        'citas_pendientes': citas_sin_telemedicina,
        'hoy': hoy,
    })


@login_required
def telemedicina_crear(request):
    form = ConsultaVirtualForm(request.POST or None)
    if form.is_valid():
        form.save()
        messages.success(request, 'Teleconsulta programada correctamente.')
        return redirect('telemedicina_lista')
        
    cita_id = request.GET.get('cita')
    if cita_id:
        form.initial['cita'] = cita_id
        
    return render(request, 'telemedicina/form.html', {'form': form, 'titulo': 'Programar Teleconsulta'})


@login_required
def telemedicina_editar(request, pk):
    consulta = get_object_or_404(ConsultaVirtual, pk=pk)
    form = ConsultaVirtualForm(request.POST or None, instance=consulta)
    if form.is_valid():
        form.save()
        messages.success(request, 'Datos de la teleconsulta actualizados.')
        return redirect('telemedicina_lista')
        
    return render(request, 'telemedicina/form.html', {'form': form, 'titulo': 'Editar Teleconsulta', 'consulta': consulta})


@login_required
def telemedicina_estado(request, pk, estado):
    consulta = get_object_or_404(ConsultaVirtual, pk=pk)
    if estado in dict(ConsultaVirtual.ESTADOS).keys():
        consulta.estado_conexion = estado
        consulta.save()
        messages.success(request, f'Estado de teleconsulta actualizado a {consulta.get_estado_conexion_display()}')
    return redirect('telemedicina_lista')
