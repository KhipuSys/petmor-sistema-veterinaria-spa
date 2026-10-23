from django.shortcuts import render, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from apps.configuracion.models import Configuracion
from apps.configuracion.forms import ConfiguracionForm


@login_required
def configuracion_view(request):
    if not request.user.is_superuser:
        messages.error(request, 'No tienes permisos para acceder a esta sección.')
        return redirect('dashboard')

    config = Configuracion.get_config()
    form = ConfiguracionForm(request.POST or None, request.FILES or None, instance=config)

    if form.is_valid():
        form.save()
        messages.success(request, 'Configuración guardada correctamente.')
        return redirect('configuracion')

    return render(request, 'configuracion/configuracion.html', {'form': form, 'config': config})
