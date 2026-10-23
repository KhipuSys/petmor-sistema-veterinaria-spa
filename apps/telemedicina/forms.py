from django import forms
from apps.telemedicina.models import ConsultaVirtual
from apps.agenda.models import Cita

class ConsultaVirtualForm(forms.ModelForm):
    class Meta:
        model = ConsultaVirtual
        fields = ['cita', 'plataforma', 'enlace_reunion', 'codigo_acceso', 'estado_conexion', 'notas_preliminares']
        widgets = {
            'cita': forms.Select(attrs={'class': 'form-select'}),
            'plataforma': forms.Select(attrs={'class': 'form-select'}),
            'enlace_reunion': forms.URLInput(attrs={'class': 'form-control', 'placeholder': 'https://meet.google.com/...'}),
            'codigo_acceso': forms.TextInput(attrs={'class': 'form-control'}),
            'estado_conexion': forms.Select(attrs={'class': 'form-select'}),
            'notas_preliminares': forms.Textarea(attrs={'class': 'form-control', 'rows': 3}),
        }

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        # Mostrar solo citas de tipo consulta y que sean futuras o del día
        self.fields['cita'].queryset = Cita.objects.filter(tipo='CONSULTA', consulta_virtual__isnull=True).order_by('-fecha', '-hora')
        
        # Si se esta editando, hay que asegurar que la cita actual este en el queryset
        if self.instance and self.instance.pk:
            self.fields['cita'].queryset = Cita.objects.filter(models.Q(tipo='CONSULTA', consulta_virtual__isnull=True) | models.Q(pk=self.instance.cita_id)).order_by('-fecha', '-hora')
