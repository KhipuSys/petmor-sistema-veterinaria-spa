import uuid
from django import forms
from django.contrib import messages
from django.contrib.auth.decorators import login_required
from django.core.exceptions import ValidationError
from django.db import IntegrityError
from django.shortcuts import get_object_or_404, render, redirect
from apps.inventario.models import Producto
from .models import CatalogoServicio, ServicioGrooming
from .services import consumir_insumo


class CatalogoForm(forms.ModelForm):
    class Meta:
        model = CatalogoServicio
        fields = ['nombre', 'area', 'descripcion', 'precio', 'duracion_minutos', 'activo']
        widgets = {'descripcion': forms.Textarea(attrs={'rows': 3})}


class ConsumoForm(forms.Form):
    producto = forms.ModelChoiceField(Producto.objects.filter(is_active=True).order_by('nombre'))
    cantidad = forms.IntegerField(min_value=1, max_value=1000000,
                                  help_text='Unidades enteras según el inventario; registre ml o dosis como unidad si corresponde.')
    clave = forms.UUIDField(widget=forms.HiddenInput)


@login_required
def catalogo(request):
    return render(request, 'grooming/catalogo.html', {'servicios': CatalogoServicio.objects.order_by('area', 'nombre')})


@login_required
def servicio_form(request, pk=None):
    servicio = get_object_or_404(CatalogoServicio, pk=pk) if pk else None
    form = CatalogoForm(request.POST or None, instance=servicio)
    if form.is_valid():
        form.save()
        messages.success(request, 'Servicio guardado.')
        return redirect('servicio_catalogo')
    return render(request, 'grooming/servicio_form.html', {'form': form})


@login_required
def consumos(request, pk):
    orden = get_object_or_404(ServicioGrooming.objects.select_related('cita__mascota'), pk=pk)
    form = ConsumoForm(request.POST or None, initial={'clave': uuid.uuid4()})
    if form.is_valid():
        try:
            consumir_insumo(orden=orden, usuario=request.user, **form.cleaned_data)
        except ValidationError as exc:
            form.add_error(None, exc)
        except IntegrityError:
            form.add_error(None, 'Este consumo ya fue registrado.')
        else:
            messages.success(request, 'Insumo descontado y registrado en kardex.')
            return redirect('grooming_consumos', pk=pk)
    registros = orden.consumos.select_related('movimiento__producto', 'movimiento__usuario')
    return render(request, 'grooming/consumos.html', {'orden': orden, 'form': form, 'consumos': registros,
                                                    'costo_total': sum(c.costo_total for c in registros)})
