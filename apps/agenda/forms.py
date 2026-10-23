from django import forms
from apps.agenda.models import Cita
from apps.clientes.models import Mascota
from django.contrib.auth.models import User


class CitaForm(forms.ModelForm):
    class Meta:
        model = Cita
        fields = ['mascota', 'veterinario', 'fecha', 'hora', 'motivo', 'tipo', 'estado', 'observaciones']
        widgets = {
            'mascota': forms.Select(attrs={'class': 'form-select'}),
            'veterinario': forms.Select(attrs={'class': 'form-select'}),
            'fecha': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
            'hora': forms.TimeInput(attrs={'class': 'form-control', 'type': 'time'}),
            'motivo': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Motivo de la consulta'}),
            'tipo': forms.Select(attrs={'class': 'form-select'}),
            'estado': forms.Select(attrs={'class': 'form-select'}),
            'observaciones': forms.Textarea(attrs={'class': 'form-control', 'rows': 3}),
        }

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['mascota'].queryset = Mascota.objects.select_related('cliente').order_by('nombre')
        self.fields['mascota'].label_from_instance = lambda obj: f"{obj.nombre} ({obj.cliente.nombre_completo})"
        self.fields['veterinario'].queryset = User.objects.filter(is_staff=True)
        self.fields['veterinario'].required = False
