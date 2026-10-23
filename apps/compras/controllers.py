from django.contrib import messages
from django.contrib.auth.decorators import login_required
from django.core.exceptions import ValidationError
from django.core.paginator import Paginator
from django.db import IntegrityError
from django.db.models import Q
from django.shortcuts import get_object_or_404, redirect, render
from django.views.decorators.http import require_POST
from .models import Compra
from .forms import CompraForm, LineasCompra
from .services import recibir_compra, anular_compra


@login_required
def lista(request):
    compras = Compra.objects.select_related('proveedor')
    q = request.GET.get('q', '').strip()
    if q:
        compras = compras.filter(Q(comprobante__icontains=q) | Q(proveedor__nombre__icontains=q))
    return render(request, 'compras/lista.html', {'compras': Paginator(compras, 30).get_page(request.GET.get('page')), 'q': q})


@login_required
def crear(request):
    form = CompraForm(request.POST or None)
    lineas = LineasCompra(request.POST or None, prefix='lineas')
    if request.method == 'POST' and form.is_valid() and lineas.is_valid():
        try:
            compra = recibir_compra(**form.cleaned_data, usuario=request.user,
                                    lineas=[d for d in lineas.cleaned_data if d and not d.get('DELETE')])
        except ValidationError as exc:
            form.add_error(None, exc)
        except IntegrityError:
            form.add_error(None, 'Ya existe este comprobante para el proveedor. Verifique antes de volver a registrar.')
        else:
            messages.success(request, 'Compra recibida: existencias y kardex actualizados.')
            return redirect('compra_detalle', pk=compra.pk)
    return render(request, 'compras/form.html', {'form': form, 'lineas': lineas})


@login_required
def detalle(request, pk):
    compra = get_object_or_404(Compra.objects.select_related('proveedor').prefetch_related('detalles__producto'), pk=pk)
    return render(request, 'compras/detalle.html', {'compra': compra})


@login_required
@require_POST
def anular(request, pk):
    get_object_or_404(Compra, pk=pk)
    try:
        anular_compra(pk, request.user)
    except ValidationError as exc:
        messages.error(request, '; '.join(exc.messages))
    else:
        messages.success(request, 'Compra anulada y existencias revertidas.')
    return redirect('compra_detalle', pk=pk)
