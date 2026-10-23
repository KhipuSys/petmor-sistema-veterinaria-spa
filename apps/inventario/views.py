from django.shortcuts import render, get_object_or_404, redirect
from django.contrib.auth.decorators import login_required
from django.contrib import messages
from django.db.models import Q
from django.core.exceptions import ValidationError
from django.core.paginator import Paginator
from django import forms
from apps.inventario.models import Producto, Proveedor, Categoria, Movimiento
from apps.inventario.forms import ProductoForm, ProveedorForm, MovimientoForm, CategoriaForm


@login_required
def producto_lista(request):
    q = request.GET.get('q', '')
    productos = Producto.objects.select_related('categoria', 'proveedor').order_by('nombre')
    if q:
        productos = productos.filter(Q(nombre__icontains=q) | Q(codigo__icontains=q) | Q(categoria__nombre__icontains=q))
    area = request.GET.get('area', '')
    if area in dict(Producto._meta.get_field('area').choices):
        productos = productos.filter(area=area)
    stock_bajo = sum(1 for p in Producto.objects.filter(is_active=True) if p.necesita_reposicion)
    return render(request, 'inventario/productos.html', {
        'productos': productos, 'q': q, 'stock_bajo': stock_bajo, 'area': area,
        'areas': Producto._meta.get_field('area').choices,
    })


@login_required
def producto_crear(request):
    form = ProductoForm(request.POST or None)
    if form.is_valid():
        form.save()
        messages.success(request, 'Producto registrado correctamente.')
        return redirect('producto_lista')
    return render(request, 'inventario/producto_form.html', {'form': form, 'titulo': 'Nuevo Producto'})


@login_required
def producto_editar(request, pk):
    producto = get_object_or_404(Producto, pk=pk)
    form = ProductoForm(request.POST or None, instance=producto)
    if form.is_valid():
        actualizado = form.save(commit=False)
        actualizado.save(update_fields=[field for field in form.Meta.fields if field != 'stock_actual'])
        messages.success(request, 'Producto actualizado.')
        return redirect('producto_lista')
    return render(request, 'inventario/producto_form.html', {'form': form, 'titulo': 'Editar Producto', 'producto': producto})


@login_required
def producto_estado(request, pk):
    if request.method == 'POST':
        producto = get_object_or_404(Producto, pk=pk)
        producto.is_active = not producto.is_active
        producto.save(update_fields=['is_active'])
        estado_str = "activado" if producto.is_active else "desactivado"
        messages.success(request, f'Producto "{producto.nombre}" {estado_str}.')
    return redirect('producto_lista')


@login_required
def proveedor_lista(request):
    proveedores = Proveedor.objects.all().order_by('nombre')
    q = request.GET.get('q', '').strip()
    if q:
        proveedores = proveedores.filter(Q(nombre__icontains=q) | Q(ruc__icontains=q))
    return render(request, 'inventario/proveedores.html', {'proveedores': proveedores, 'q': q})


@login_required
def proveedor_crear(request):
    form = ProveedorForm(request.POST or None)
    if form.is_valid():
        form.save()
        messages.success(request, 'Proveedor registrado.')
        return redirect('proveedor_lista')
    return render(request, 'inventario/proveedor_form.html', {'form': form, 'titulo': 'Nuevo Proveedor'})


@login_required
def proveedor_editar(request, pk):
    proveedor = get_object_or_404(Proveedor, pk=pk)
    form = ProveedorForm(request.POST or None, instance=proveedor)
    if form.is_valid():
        form.save()
        messages.success(request, 'Proveedor actualizado.')
        return redirect('proveedor_lista')
    return render(request, 'inventario/proveedor_form.html', {'form': form, 'titulo': 'Editar Proveedor'})


@login_required
def movimiento_lista(request):
    class Filtro(forms.Form):
        producto = forms.ModelChoiceField(Producto.objects.order_by('nombre'), required=False)
        desde = forms.DateField(required=False, widget=forms.DateInput(attrs={'type': 'date'}))
        hasta = forms.DateField(required=False, widget=forms.DateInput(attrs={'type': 'date'}))
    filtro = Filtro(request.GET)
    movimientos = Movimiento.objects.select_related('producto', 'usuario').order_by('-fecha', '-pk')
    if filtro.is_valid():
        data = filtro.cleaned_data
        if data['producto']:
            movimientos = movimientos.filter(producto=data['producto'])
        if data['desde']:
            movimientos = movimientos.filter(fecha__date__gte=data['desde'])
        if data['hasta']:
            movimientos = movimientos.filter(fecha__date__lte=data['hasta'])
    else:
        movimientos = movimientos.none()
    pagina = Paginator(movimientos, 50).get_page(request.GET.get('page'))
    query = request.GET.copy()
    query.pop('page', None)
    return render(request, 'inventario/kardex.html', {'movimientos': pagina, 'filtro': filtro, 'query': query.urlencode()})


@login_required
def movimiento_crear(request):
    form = MovimientoForm(request.POST or None)
    if form.is_valid():
        movimiento = form.save(commit=False)
        movimiento.usuario = request.user
        try:
            movimiento.save()
        except ValidationError as exc:
            form.add_error(None, exc)
        else:
            messages.success(request, 'Movimiento registrado. Stock actualizado.')
            return redirect('movimiento_lista')
    return render(request, 'inventario/movimiento_form.html', {'form': form, 'titulo': 'Registrar Movimiento'})


@login_required
def categoria_lista(request):
    categorias = Categoria.objects.all().order_by('nombre')
    return render(request, 'inventario/categorias.html', {'categorias': categorias})


@login_required
def categoria_crear(request):
    form = CategoriaForm(request.POST or None)
    if form.is_valid():
        form.save()
        messages.success(request, 'Categoría creada.')
        return redirect('categoria_lista')
    return render(request, 'inventario/categoria_form.html', {'form': form})
