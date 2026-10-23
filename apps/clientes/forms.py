from django import forms
from apps.clientes.models import Cliente, Mascota


class ClienteForm(forms.ModelForm):
    def clean_dni(self):
        documento = (self.cleaned_data.get('dni') or '').strip()
        if not documento:
            raise forms.ValidationError('Este campo es obligatorio.')
        if not documento.isdigit() or len(documento) not in (8, 11):
            raise forms.ValidationError('Ingrese un DNI de 8 dígitos o un RUC de 11 dígitos.')
        return documento

    def clean_telefono(self):
        telefono = (self.cleaned_data.get('telefono') or '').strip()
        if not telefono:
            raise forms.ValidationError('Este campo es obligatorio.')
        return telefono

    class Meta:
        model = Cliente
        fields = ['nombres', 'apellidos', 'dni', 'telefono', 'email', 'direccion']
        error_messages = {
            'nombres': {'required': 'Este campo es obligatorio.'},
            'apellidos': {'required': 'Este campo es obligatorio.'},
            'telefono': {'required': 'Este campo es obligatorio.'},
        }
        widgets = {
            'nombres': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Nombres'}),
            'apellidos': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Apellidos'}),
            'dni': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'DNI o RUC', 'maxlength': '11', 'inputmode': 'numeric'}),
            'telefono': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Teléfono'}),
            'email': forms.EmailInput(attrs={'class': 'form-control', 'placeholder': 'correo@ejemplo.com'}),
            'direccion': forms.Textarea(attrs={'class': 'form-control', 'rows': 3, 'placeholder': 'Dirección'}),
        }


class MascotaForm(forms.ModelForm):
    class Meta:
        model = Mascota
        fields = ['nombre', 'especie', 'raza', 'sexo', 'fecha_nacimiento', 'peso_actual', 'color', 'microchip', 'foto']
        widgets = {
            'nombre': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Nombre de la mascota'}),
            'especie': forms.Select(attrs={'class': 'form-select'}),
            'raza': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Raza (opcional)'}),
            'sexo': forms.Select(attrs={'class': 'form-select'}),
            'fecha_nacimiento': forms.DateInput(format='%Y-%m-%d', attrs={'class': 'form-control', 'type': 'date'}),
            'peso_actual': forms.NumberInput(attrs={'class': 'form-control', 'step': '0.01', 'placeholder': 'Kg'}),
            'color': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'Color/pelaje'}),
            'microchip': forms.TextInput(attrs={'class': 'form-control', 'placeholder': 'N° de microchip'}),
            'foto': forms.FileInput(attrs={'class': 'form-control'}),
        }
