from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.utils import timezone
from apps.grooming.models import ServicioGrooming
from apps.grooming.forms import ServicioGroomingForm
from apps.agenda.models import Cita


@login_required
def grooming_lista(request):
    hoy = timezone.localdate()
    ordenes = ServicioGrooming.objects.select_related('cita__mascota__cliente', 'peluquero').filter(is_active=True).order_by('-cita__fecha')
    citas_grooming_sin_orden = Cita.objects.filter(tipo='GROOMING').exclude(
        pk__in=ServicioGrooming.objects.values_list('cita_id', flat=True)
    ).order_by('-fecha')
    return render(request, 'grooming/lista.html', {
        'ordenes': ordenes,
        'citas_pendientes': citas_grooming_sin_orden,
        'hoy': hoy,
    })


@login_required
def grooming_crear(request):
    form = ServicioGroomingForm(request.POST or None)
    if form.is_valid():
        form.save()
        messages.success(request, 'Orden de grooming registrada.')
        return redirect('grooming_lista')
    cita_pk = request.GET.get('cita')
    if cita_pk:
        form.initial['cita'] = cita_pk
    return render(request, 'grooming/orden_form.html', {'form': form, 'titulo': 'Nueva Orden de Grooming'})


@login_required
def grooming_editar(request, pk):
    orden = get_object_or_404(ServicioGrooming, pk=pk)
    form = ServicioGroomingForm(request.POST or None, instance=orden)
    if form.is_valid():
        form.save()
        messages.success(request, 'Orden de grooming actualizada.')
        return redirect('grooming_lista')
    return render(request, 'grooming/orden_form.html', {'form': form, 'titulo': 'Editar Orden de Grooming', 'orden': orden})


@login_required
def grooming_estado(request, pk):
    if request.method == 'POST':
        orden = get_object_or_404(ServicioGrooming, pk=pk)
        orden.is_active = not orden.is_active
        orden.save()
        estado_str = "activado" if orden.is_active else "desactivado"
        messages.success(request, f'Servicio de Grooming {estado_str}.')
    return redirect('grooming_lista')
