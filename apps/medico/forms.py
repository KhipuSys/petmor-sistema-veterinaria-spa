from django import forms
from django.forms import inlineformset_factory
from apps.medico.models import HistoriaClinica, Vacuna, Receta, DetalleReceta
from apps.clientes.models import Mascota
from django.contrib.auth.models import User


class HistoriaClinicaForm(forms.ModelForm):
    class Meta:
        model = HistoriaClinica
        fields = [
            'mascota', 'veterinario', 'motivo_consulta', 'anamnesis',
            'peso', 'temperatura', 'frecuencia_cardiaca', 'frecuencia_respiratoria',
            'mucosas', 'tiempo_llenado_capilar',
            'diagnostico', 'tratamiento', 'observaciones', 'proxima_cita'
        ]
        widgets = {
            'mascota': forms.Select(attrs={'class': 'form-select'}),
            'veterinario': forms.Select(attrs={'class': 'form-select'}),
            'motivo_consulta': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Motivo de la consulta'}),
            'anamnesis': forms.Textarea(attrs={'class': 'form-control', 'rows': 3, 'placeholder': 'Antecedentes y síntomas'}),
            'peso': forms.NumberInput(attrs={'class': 'form-control', 'step': '0.01', 'placeholder': 'Kg'}),
            'temperatura': forms.NumberInput(attrs={'class': 'form-control', 'step': '0.1', 'placeholder': '°C'}),
            'frecuencia_cardiaca': forms.NumberInput(attrs={'class': 'form-control', 'placeholder': 'BPM'}),
            'frecuencia_respiratoria': forms.NumberInput(attrs={'class': 'form-control', 'placeholder': 'RPM'}),
            'mucosas': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Ej: Rosadas'}),
            'tiempo_llenado_capilar': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Ej: < 2 seg'}),
            'diagnostico': forms.Textarea(attrs={'class': 'form-control', 'rows': 3}),
            'tratamiento': forms.Textarea(attrs={'class': 'form-control', 'rows': 3}),
            'observaciones': forms.Textarea(attrs={'class': 'form-control', 'rows': 2}),
            'proxima_cita': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
        }

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['mascota'].queryset = Mascota.objects.select_related('cliente').order_by('nombre')
        self.fields['mascota'].label_from_instance = lambda obj: f"{obj.nombre} ({obj.cliente.nombre_completo})"
        self.fields['veterinario'].queryset = User.objects.filter(is_staff=True)
        self.fields['veterinario'].required = False


class VacunaForm(forms.ModelForm):
    class Meta:
        model = Vacuna
        fields = ['mascota', 'nombre_vacuna', 'lote', 'fecha_aplicacion', 'fecha_proxima_dosis', 'veterinario', 'observaciones']
        widgets = {
            'mascota': forms.Select(attrs={'class': 'form-select'}),
            'nombre_vacuna': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Nombre de la vacuna'}),
            'lote': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'N° de lote'}),
            'fecha_aplicacion': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
            'fecha_proxima_dosis': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
            'veterinario': forms.Select(attrs={'class': 'form-select'}),
            'observaciones': forms.Textarea(attrs={'class': 'form-control', 'rows': 2}),
        }

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['mascota'].queryset = Mascota.objects.select_related('cliente').order_by('nombre')
        self.fields['mascota'].label_from_instance = lambda obj: f"{obj.nombre} ({obj.cliente.nombre_completo})"
        self.fields['veterinario'].queryset = User.objects.filter(is_staff=True)
        self.fields['veterinario'].required = False


class DetalleRecetaForm(forms.ModelForm):
    class Meta:
        model = DetalleReceta
        fields = ['medicamento', 'dosis', 'frecuencia', 'duracion', 'cantidad']
        widgets = {
            'medicamento': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Medicamento'}),
            'dosis': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Dosis'}),
            'frecuencia': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Frecuencia'}),
            'duracion': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Duración'}),
            'cantidad': forms.NumberInput(attrs={'class': 'form-control'}),
        }

DetalleRecetaFormSet = inlineformset_factory(
    Receta, DetalleReceta,
    form=DetalleRecetaForm,
    extra=2, can_delete=True
)
