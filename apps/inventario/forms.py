from django import forms
from apps.inventario.models import Producto, Proveedor, Categoria, Movimiento


class CategoriaForm(forms.ModelForm):
    class Meta:
        model = Categoria
        fields = ['nombre', 'descripcion']
        widgets = {
            'nombre': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Nombre de la categoría'}),
            'descripcion': forms.Textarea(attrs={'class': 'form-control', 'rows': 2}),
        }


class ProveedorForm(forms.ModelForm):
    def clean_ruc(self):
        ruc = (self.cleaned_data.get('ruc') or '').strip()
        if ruc and (len(ruc) != 11 or not ruc.isascii() or not ruc.isdigit()):
            raise forms.ValidationError('El RUC debe tener 11 dígitos.')
        return ruc or None

    class Meta:
        model = Proveedor
        fields = ['nombre', 'ruc', 'telefono', 'email', 'direccion', 'contacto']
        widgets = {
            'nombre': forms.TextInput(attrs={'class': 'form-control'}),
            'ruc': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'RUC'}),
            'telefono': forms.TextInput(attrs={'class': 'form-control'}),
            'email': forms.EmailInput(attrs={'class': 'form-control'}),
            'direccion': forms.Textarea(attrs={'class': 'form-control', 'rows': 2}),
            'contacto': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Persona de contacto'}),
        }


class ProductoForm(forms.ModelForm):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['stock_actual'].disabled = True
        self.fields['stock_actual'].help_text = 'Las existencias se registran mediante compras o kardex.'
        for nombre in ('precio_compra', 'precio_venta', 'stock_minimo'):
            self.fields[nombre].min_value = 0

    def clean(self):
        data = super().clean()
        for nombre in ('precio_compra', 'precio_venta', 'stock_minimo'):
            if data.get(nombre) is not None and data[nombre] < 0:
                self.add_error(nombre, 'No puede ser negativo.')
        if self.instance.pk and 'unidad' in self.changed_data and self.instance.movimientos.exists():
            self.add_error('unidad', 'Este producto ya tiene kardex. Cree otro producto si necesita cambiar la unidad.')
        return data

    class Meta:
        model = Producto
        fields = ['codigo', 'nombre', 'categoria', 'proveedor', 'descripcion', 'area', 'unidad',
                  'precio_compra', 'precio_venta', 'stock_actual', 'stock_minimo',
                  'fecha_vencimiento', 'is_active']
        widgets = {
            'codigo': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Código (opcional)'}),
            'area': forms.Select(attrs={'class': 'form-select'}),
            'unidad': forms.TextInput(attrs={'class': 'form-control'}),
            'nombre': forms.TextInput(attrs={'class': 'form-control'}),
            'categoria': forms.Select(attrs={'class': 'form-select'}),
            'proveedor': forms.Select(attrs={'class': 'form-select'}),
            'descripcion': forms.Textarea(attrs={'class': 'form-control', 'rows': 2}),
            'precio_compra': forms.NumberInput(attrs={'class': 'form-control', 'step': '0.01'}),
            'precio_venta': forms.NumberInput(attrs={'class': 'form-control', 'step': '0.01'}),
            'stock_actual': forms.NumberInput(attrs={'class': 'form-control'}),
            'stock_minimo': forms.NumberInput(attrs={'class': 'form-control'}),
            'fecha_vencimiento': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
            'is_active': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
        }


class MovimientoForm(forms.ModelForm):
    def clean_cantidad(self):
        cantidad = self.cleaned_data['cantidad']
        if cantidad < 0 or (self.cleaned_data.get('tipo') != 'AJUSTE' and cantidad == 0):
            raise forms.ValidationError('Ingrese una cantidad válida mayor que cero, o cero para ajuste.')
        return cantidad

    class Meta:
        model = Movimiento
        fields = ['producto', 'tipo', 'cantidad', 'motivo', 'comprobante']
        widgets = {
            'producto': forms.Select(attrs={'class': 'form-select'}),
            'tipo': forms.Select(attrs={'class': 'form-select'}),
            'cantidad': forms.NumberInput(attrs={'class': 'form-control', 'min': '0'}),
            'motivo': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Motivo del movimiento'}),
            'comprobante': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'N° comprobante (opcional)'}),
        }
