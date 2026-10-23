from apps.configuracion.models import Configuracion


def configuracion(request):
    """Context processor que inyecta la configuración en todos los templates."""
    try:
        config = Configuracion.get_config()
    except Exception:
        config = None
    return {'configuracion': config}
