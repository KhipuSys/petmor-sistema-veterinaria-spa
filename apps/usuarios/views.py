from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required, user_passes_test
from django.contrib import messages
from django.contrib.auth.models import User
from apps.usuarios.forms import UsuarioCreateForm, UsuarioEditForm


def is_admin(user):
    return user.is_superuser


@login_required
@user_passes_test(is_admin, login_url='/dashboard/')
def usuario_lista(request):
    usuarios = User.objects.all().order_by('-is_active', 'is_superuser', 'first_name')
    return render(request, 'usuarios/lista.html', {'usuarios': usuarios})


@login_required
@user_passes_test(is_admin, login_url='/dashboard/')
def usuario_crear(request):
    form = UsuarioCreateForm(request.POST or None)
    if form.is_valid():
        user = form.save()
        messages.success(request, f'Usuario {user.username} creado correctamente.')
        return redirect('usuario_lista')
    return render(request, 'usuarios/form.html', {'form': form, 'titulo': 'Nuevo Usuario'})


@login_required
@user_passes_test(is_admin, login_url='/dashboard/')
def usuario_editar(request, pk):
    usuario = get_object_or_404(User, pk=pk)
    form = UsuarioEditForm(request.POST or None, instance=usuario)
    if form.is_valid():
        form.save()
        messages.success(request, f'Datos de {usuario.username} actualizados.')
        return redirect('usuario_lista')
    return render(request, 'usuarios/form.html', {'form': form, 'titulo': 'Editar Usuario', 'usuario_instance': usuario})


@login_required
@user_passes_test(is_admin, login_url='/dashboard/')
def usuario_estado(request, pk):
    if request.method == 'POST':
        usuario = get_object_or_404(User, pk=pk)
        if usuario.pk == request.user.pk:
            messages.error(request, 'No puedes desactivar tu propia cuenta actual.')
        else:
            usuario.is_active = not usuario.is_active
            usuario.save()
            estado = "activado" if usuario.is_active else "desactivado"
            messages.success(request, f'El usuario {usuario.username} ha sido {estado}.')
    return redirect('usuario_lista')
