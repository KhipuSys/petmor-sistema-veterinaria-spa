from django import forms
from django.forms import formset_factory
from django.utils import timezone
from apps.inventario.models import Producto
from .models import Compra


class CompraForm(forms.ModelForm):
    class Meta:
        model = Compra
        fields = ['proveedor', 'comprobante', 'fecha', 'observaciones']
        widgets = {'fecha': forms.DateInput(format='%Y-%m-%d', attrs={'type': 'date'}),
                   'observaciones': forms.Textarea(attrs={'rows': 2})}

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['fecha'].initial = timezone.localdate


class LineaCompraForm(forms.Form):
    producto = forms.ModelChoiceField(Producto.objects.filter(is_active=True).order_by('nombre'))
    cantidad = forms.IntegerField(min_value=1, max_value=1000000)
    costo_unitario = forms.DecimalField(min_value=0, max_digits=8, decimal_places=2,
                                       label='Costo unitario final (S/)')


LineasCompra = formset_factory(LineaCompraForm, extra=1, min_num=1, validate_min=True,
                              max_num=100, validate_max=True, can_delete=True)
