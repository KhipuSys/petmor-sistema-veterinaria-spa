from django import forms
from apps.grooming.models import ServicioGrooming, CatalogoServicio
from apps.agenda.models import Cita
from django.contrib.auth.models import User


class ServicioGroomingForm(forms.ModelForm):
    class Meta:
        model = ServicioGrooming
        fields = ['cita', 'servicio', 'peluquero', 'bano', 'corte_pelo', 'corte_unas',
                  'limpieza_oidos', 'glandulas', 'perfume', 'observaciones',
                  'fecha_inicio', 'fecha_fin']
        widgets = {
            'cita': forms.Select(attrs={'class': 'form-select'}),
            'peluquero': forms.Select(attrs={'class': 'form-select'}),
            'bano': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'corte_pelo': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'corte_unas': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'limpieza_oidos': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'glandulas': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'perfume': forms.CheckboxInput(attrs={'class': 'form-check-input'}),
            'observaciones': forms.Textarea(attrs={'class': 'form-control', 'rows': 3}),
            'fecha_inicio': forms.DateTimeInput(format='%Y-%m-%dT%H:%M', attrs={'class': 'form-control', 'type': 'datetime-local'}),
            'fecha_fin': forms.DateTimeInput(format='%Y-%m-%dT%H:%M', attrs={'class': 'form-control', 'type': 'datetime-local'}),
        }

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.fields['cita'].queryset = Cita.objects.filter(tipo='GROOMING').order_by('-fecha')
        self.fields['cita'].label_from_instance = lambda obj: f"{obj.mascota.nombre} - {obj.fecha} {obj.hora}"
        self.fields['peluquero'].queryset = User.objects.filter(is_staff=True)
        self.fields['peluquero'].required = False
        self.fields['fecha_inicio'].required = False
        self.fields['fecha_fin'].required = False
        self.fields['servicio'].queryset = CatalogoServicio.objects.filter(area='SPA', activo=True)
        self.fields['servicio'].widget.attrs['class'] = 'form-select'

    def clean(self):
        data = super().clean()
        if data.get('fecha_fin') and not data.get('fecha_inicio'):
            self.add_error('fecha_inicio', 'Indique cuándo inició la atención.')
        if data.get('fecha_fin') and data.get('fecha_inicio') and data['fecha_fin'] < data['fecha_inicio']:
            self.add_error('fecha_fin', 'El fin no puede ser anterior al inicio.')
        return data
