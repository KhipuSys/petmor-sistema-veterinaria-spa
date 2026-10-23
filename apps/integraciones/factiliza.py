"""Consultas DNI/RUC siguiendo el adaptador de MiniMarket; sin emisión fiscal."""
import json
import re
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from django.conf import settings


class FactilizaError(Exception):
    pass


def consultar_documento(documento):
    documento = documento.strip()
    if not re.fullmatch(r'(?:[0-9]{8}|[0-9]{11})', documento):
        raise FactilizaError('Ingrese un DNI de 8 dígitos o RUC de 11 dígitos.')
    token = settings.FACTILIZA_TOKEN
    if not token:
        raise FactilizaError('Factiliza aún no está configurada. Puede completar los datos manualmente.')
    tipo = 'dni' if len(documento) == 8 else 'ruc'
    request = Request(f'https://api.factiliza.com/v1/{tipo}/info/{documento}',
                      headers={'Authorization': f'Bearer {token}', 'Accept': 'application/json'})
    try:
        with urlopen(request, timeout=10) as response:
            payload = json.loads(response.read(262144).decode('utf-8'))
    except HTTPError as exc:
        if exc.code in (401, 403):
            raise FactilizaError('Factiliza rechazó las credenciales o el permiso de consulta.') from None
        if exc.code == 429:
            raise FactilizaError('Se alcanzó el límite de consultas. Intente más tarde.') from None
        raise FactilizaError('No se pudo consultar el documento en Factiliza.') from None
    except (URLError, TimeoutError, OSError, ValueError, UnicodeError):
        raise FactilizaError('Factiliza no respondió correctamente. Complete los datos manualmente.') from None
    if not isinstance(payload, dict):
        raise FactilizaError('Respuesta no válida de Factiliza.')
    data = payload.get('data', payload)
    if not isinstance(data, dict):
        raise FactilizaError('No se encontraron datos para este documento.')

    def text(*keys):
        return next((data[k].strip() for k in keys if isinstance(data.get(k), str) and data[k].strip()), '')

    nombres = text('nombres', 'prenombres') if tipo == 'dni' else ''
    apellidos = ' '.join(filter(None, [text('apellido_paterno', 'apellidoPaterno'), text('apellido_materno', 'apellidoMaterno')])) if tipo == 'dni' else ''
    nombre = text('nombre_o_razon_social', 'razon_social', 'nombre_completo', 'nombre', 'nombres')
    if tipo == 'ruc' and documento.startswith('10') and not nombres and not apellidos:
        partes = nombre.split()
        if len(partes) >= 3:
            apellidos = ' '.join(partes[:2])
            nombres = ' '.join(partes[2:])
    if nombres and apellidos:
        nombre = f'{nombres} {apellidos}'
    if not nombre:
        raise FactilizaError('La consulta no devolvió el nombre del titular.')
    return {'documento': documento, 'nombre': nombre, 'nombres': nombres, 'apellidos': apellidos,
            'direccion': text('direccion_completa', 'direccion', 'domicilio_fiscal')}
