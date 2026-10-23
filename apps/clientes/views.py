from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.db.models import Q
from apps.clientes.models import Cliente, Mascota
from apps.clientes.forms import ClienteForm, MascotaForm


@login_required
def cliente_lista(request):
    q = request.GET.get('q', '')
    clientes = Cliente.objects.all().order_by('-fecha_registro')
    if q:
        clientes = clientes.filter(
            Q(nombres__icontains=q) | Q(apellidos__icontains=q) | Q(dni__icontains=q) | Q(telefono__icontains=q)
        )
    return render(request, 'clientes/lista.html', {'clientes': clientes, 'q': q})


@login_required
def cliente_crear(request):
    form = ClienteForm(request.POST or None)
    if form.is_valid():
        cliente = form.save()
        messages.success(request, f'Cliente {cliente.nombre_completo} registrado correctamente.')
        return redirect('cliente_perfil', pk=cliente.pk)
    return render(request, 'clientes/form.html', {'form': form, 'titulo': 'Nuevo Cliente', 'accion': 'Registrar'})


@login_required
def cliente_editar(request, pk):
    cliente = get_object_or_404(Cliente, pk=pk)
    form = ClienteForm(request.POST or None, instance=cliente)
    if form.is_valid():
        form.save()
        messages.success(request, 'Cliente actualizado correctamente.')
        return redirect('cliente_perfil', pk=cliente.pk)
    return render(request, 'clientes/form.html', {'form': form, 'titulo': 'Editar Cliente', 'accion': 'Guardar cambios', 'cliente': cliente})


@login_required
def cliente_perfil(request, pk):
    cliente = get_object_or_404(Cliente, pk=pk)
    mascotas = cliente.mascotas.all()
    return render(request, 'clientes/perfil.html', {'cliente': cliente, 'mascotas': mascotas})


@login_required
def cliente_estado(request, pk):
    if request.method == 'POST':
        cliente = get_object_or_404(Cliente, pk=pk)
        cliente.is_active = not cliente.is_active
        cliente.save()
        estado_str = "activado" if cliente.is_active else "desactivado"
        messages.success(request, f'Cliente {cliente.nombre_completo} {estado_str}.')
    return redirect('cliente_lista')

@login_required
def mascota_crear(request, cliente_pk):
    cliente = get_object_or_404(Cliente, pk=cliente_pk)
    form = MascotaForm(request.POST or None, request.FILES or None)
    if form.is_valid():
        mascota = form.save(commit=False)
        mascota.cliente = cliente
        mascota.save()
        messages.success(request, f'Mascota {mascota.nombre} registrada correctamente.')
        return redirect('cliente_perfil', pk=cliente.pk)
    return render(request, 'clientes/mascota_form.html', {'form': form, 'cliente': cliente, 'titulo': 'Nueva Mascota'})


@login_required
def mascota_editar(request, pk):
    mascota = get_object_or_404(Mascota, pk=pk)
    form = MascotaForm(request.POST or None, request.FILES or None, instance=mascota)
    if form.is_valid():
        form.save()
        messages.success(request, 'Mascota actualizada correctamente.')
        return redirect('cliente_perfil', pk=mascota.cliente.pk)
    return render(request, 'clientes/mascota_form.html', {'form': form, 'cliente': mascota.cliente, 'titulo': 'Editar Mascota'})


@login_required
def mascota_estado(request, pk):
    if request.method == 'POST':
        mascota = get_object_or_404(Mascota, pk=pk)
        cliente_pk = mascota.cliente.pk
        mascota.is_active = not mascota.is_active
        mascota.save()
        estado_str = "activado" if mascota.is_active else "desactivado"
        messages.success(request, f'Mascota {mascota.nombre} {estado_str}.')
    return redirect('cliente_perfil', pk=mascota.cliente.pk)
