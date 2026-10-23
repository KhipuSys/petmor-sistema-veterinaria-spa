from django.conf import settings
from django.contrib.auth.decorators import login_required, user_passes_test
from django.http import JsonResponse
from django.shortcuts import render
from django.views.decorators.http import require_POST
from .factiliza import consultar_documento, FactilizaError


@login_required
@require_POST
def documento(request):
    try:
        data = consultar_documento(request.POST.get('documento', ''))
    except FactilizaError as exc:
        response = JsonResponse({'ok': False, 'mensaje': str(exc)}, status=400)
    else:
        response = JsonResponse({'ok': True, 'data': data})
    response['Cache-Control'] = 'no-store'
    return response


@login_required
@user_passes_test(lambda user: user.is_superuser)
def estado(request):
    return render(request, 'integraciones/estado.html', {'factiliza_configurada': bool(settings.FACTILIZA_TOKEN)})
