from django import forms
from django.contrib.auth.models import User

class UsuarioCreateForm(forms.ModelForm):
    ROLE_CHOICES = (
        ('ADMIN', 'Administrador'),
        ('VET', 'Veterinario'),
        ('RECEPCION', 'Recepcionista'),
        ('GROOMING', 'Area Grooming'),
    )
    rol = forms.ChoiceField(choices=ROLE_CHOICES, widget=forms.Select(attrs={'class': 'form-select'}))
    password = forms.CharField(widget=forms.PasswordInput(attrs={'class': 'form-control'}), label='Contraseña')

    class Meta:
        model = User
        fields = ['username', 'first_name', 'last_name', 'email', 'password']
        widgets = {
            'username': forms.TextInput(attrs={'class': 'form-control', 'autocomplete': 'new-password'}),
            'first_name': forms.TextInput(attrs={'class': 'form-control'}),
            'last_name': forms.TextInput(attrs={'class': 'form-control'}),
            'email': forms.EmailInput(attrs={'class': 'form-control'}),
        }

    def save(self, commit=True):
        user = super().save(commit=False)
        user.set_password(self.cleaned_data['password'])
        rol = self.cleaned_data['rol']
        
        if rol == 'ADMIN':
            user.is_superuser = True
            user.is_staff = True
        elif rol == 'VET':
            user.is_staff = True
        else:
            user.is_staff = False
            user.is_superuser = False
            
        if commit:
            user.save()
            # Aqí se podrían añadir al grupo si existieran grupos formales Group.objects.get(...)
        return user


class UsuarioEditForm(forms.ModelForm):
    ROLE_CHOICES = (
        ('ADMIN', 'Administrador'),
        ('VET', 'Veterinario'),
        ('RECEPCION', 'Recepcionista'),
        ('GROOMING', 'Area Grooming'),
    )
    rol = forms.ChoiceField(choices=ROLE_CHOICES, widget=forms.Select(attrs={'class': 'form-select'}))

    class Meta:
        model = User
        fields = ['first_name', 'last_name', 'email']
        widgets = {
            'first_name': forms.TextInput(attrs={'class': 'form-control'}),
            'last_name': forms.TextInput(attrs={'class': 'form-control'}),
            'email': forms.EmailInput(attrs={'class': 'form-control'}),
        }
        
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        if self.instance.pk:
            if self.instance.is_superuser:
                self.fields['rol'].initial = 'ADMIN'
            elif self.instance.is_staff:
                self.fields['rol'].initial = 'VET'
            else:
                self.fields['rol'].initial = 'RECEPCION' # Asumimos por defecto si no es staff

    def save(self, commit=True):
        user = super().save(commit=commit)
        rol = self.cleaned_data['rol']
        
        if rol == 'ADMIN':
            user.is_superuser = True
            user.is_staff = True
        elif rol == 'VET':
            user.is_staff = True
            user.is_superuser = False
        else:
            user.is_staff = False
            user.is_superuser = False
            
        if commit:
            user.save()
        return user
