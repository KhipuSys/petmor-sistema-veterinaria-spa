from pathlib import Path

from django.conf import settings
from django.contrib.auth import get_user_model
from django.test import TestCase
from django.urls import reverse

from apps.core.management.commands.seed_demo import Command as SeedDemoCommand


class ApplicationSmokeTests(TestCase):
    @classmethod
    def setUpTestData(cls):
        SeedDemoCommand().handle(verbosity=0)

    def setUp(self):
        self.client.force_login(get_user_model().objects.get(username='admin_demo'))

    def test_main_pages_render_for_demo_admin(self):
        route_names = (
            'dashboard', 'cliente_lista', 'agenda_lista', 'historia_lista',
            'vacuna_lista', 'telemedicina_lista', 'grooming_lista',
            'servicio_catalogo', 'proveedor_lista', 'compra_lista',
            'producto_lista', 'movimiento_lista', 'factura_lista', 'caja_hoy',
            'reportes_index', 'integraciones_estado', 'usuario_lista',
            'configuracion', 'mantenimiento_index',
        )
        for route_name in route_names:
            with self.subTest(route=route_name):
                response = self.client.get(reverse(route_name))
                self.assertEqual(response.status_code, 200)

    def test_templates_use_local_frontend_assets(self):
        templates = (
            Path(settings.BASE_DIR) / 'templates' / 'base.html',
            Path(settings.BASE_DIR) / 'templates' / 'registration' / 'login.html',
            Path(settings.BASE_DIR) / 'templates' / 'dashboard' / 'index.html',
        )
        for template in templates:
            content = template.read_text(encoding='utf-8')
            self.assertNotIn('cdn.jsdelivr.net', content)
            self.assertNotIn('cdnjs.cloudflare.com', content)
            self.assertNotIn('fonts.googleapis.com', content)

        required_assets = (
            'vendor/bootstrap/css/bootstrap.min.css',
            'vendor/bootstrap/js/bootstrap.bundle.min.js',
            'vendor/fontawesome/css/all.min.css',
            'vendor/fontawesome/webfonts/fa-solid-900.woff2',
            'vendor/fontawesome/webfonts/fa-regular-400.woff2',
            'vendor/chartjs/chart.umd.min.js',
            'img/logo2pngblanco.png',
        )
        for relative_path in required_assets:
            self.assertTrue((Path(settings.BASE_DIR) / 'static' / relative_path).is_file())
