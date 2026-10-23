from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.db.models import Q
from apps.medico.models import HistoriaClinica, Vacuna, Receta, DetalleReceta
from apps.medico.forms import HistoriaClinicaForm, VacunaForm, DetalleRecetaFormSet
from apps.agenda.models import Cita
from apps.clientes.models import Mascota


@login_required
def historia_lista(request):
    q = request.GET.get('q', '')
    historias = HistoriaClinica.objects.select_related('mascota__cliente', 'veterinario').order_by('-fecha')
    if q:
        historias = historias.filter(
            Q(mascota__nombre__icontains=q) | Q(mascota__cliente__nombres__icontains=q)
            | Q(diagnostico__icontains=q)
        )
    return render(request, 'medico/lista.html', {'historias': historias, 'q': q})


@login_required
def historia_crear(request, cita_pk=None):
    cita = None
    initial = {}
    if cita_pk:
        cita = get_object_or_404(Cita, pk=cita_pk)
        initial = {'mascota': cita.mascota, 'veterinario': cita.veterinario}

    form = HistoriaClinicaForm(request.POST or None, initial=initial)
    if form.is_valid():
        historia = form.save(commit=False)
        if cita:
            historia.cita = cita
        historia.save()
        # Crear receta vacía asociada
        receta = Receta.objects.create(historia_clinica=historia)
        # Actualizar estado de la cita a COMPLETADA
        if cita:
            cita.estado = 'COMPLETADA'
            cita.save()
        messages.success(request, f'Historia clínica #{historia.pk} registrada.')
        return redirect('historia_detalle', pk=historia.pk)

    return render(request, 'medico/historia_form.html', {
        'form': form, 'cita': cita, 'titulo': 'Nueva Historia Clínica'
    })


@login_required
def historia_editar(request, pk):
    historia = get_object_or_404(HistoriaClinica, pk=pk)
    form = HistoriaClinicaForm(request.POST or None, instance=historia)
    if form.is_valid():
        form.save()
        messages.success(request, 'Historia clínica actualizada.')
        return redirect('historia_detalle', pk=historia.pk)
    return render(request, 'medico/historia_form.html', {
        'form': form, 'titulo': 'Editar Historia Clínica', 'historia': historia
    })


@login_required
def historia_detalle(request, pk):
    historia = get_object_or_404(HistoriaClinica.objects.select_related('mascota__cliente', 'veterinario', 'cita'), pk=pk)
    receta = getattr(historia, 'receta', None)
    formset = None

    if request.method == 'POST' and receta:
        formset = DetalleRecetaFormSet(request.POST, instance=receta)
        if formset.is_valid():
            formset.save()
            messages.success(request, 'Receta guardada correctamente.')
            return redirect('historia_detalle', pk=historia.pk)
    elif receta:
        formset = DetalleRecetaFormSet(instance=receta)

    return render(request, 'medico/historia_detalle.html', {
        'historia': historia,
        'receta': receta,
        'formset': formset,
    })


@login_required
def vacuna_lista(request):
    q = request.GET.get('q', '')
    vacunas = Vacuna.objects.select_related('mascota__cliente', 'veterinario').order_by('-fecha_aplicacion')
    if q:
        vacunas = vacunas.filter(
            Q(mascota__nombre__icontains=q) | Q(nombre_vacuna__icontains=q)
        )
    return render(request, 'medico/vacuna_lista.html', {'vacunas': vacunas, 'q': q})


@login_required
def vacuna_crear(request, mascota_pk=None):
    initial = {}
    mascota = None
    if mascota_pk:
        mascota = get_object_or_404(Mascota, pk=mascota_pk)
        initial = {'mascota': mascota}
    form = VacunaForm(request.POST or None, initial=initial)
    if form.is_valid():
        form.save()
        messages.success(request, 'Vacuna registrada correctamente.')
        if mascota:
            return redirect('cliente_perfil', pk=mascota.cliente.pk)
        return redirect('vacuna_lista')
    return render(request, 'medico/vacuna_form.html', {'form': form, 'mascota': mascota})


@login_required
def vacuna_editar(request, pk):
    vacuna = get_object_or_404(Vacuna, pk=pk)
    form = VacunaForm(request.POST or None, instance=vacuna)
    if form.is_valid():
        form.save()
        messages.success(request, 'Vacuna actualizada.')
        return redirect('vacuna_lista')
    return render(request, 'medico/vacuna_form.html', {'form': form, 'vacuna': vacuna})
