import os

from django.contrib.auth import get_user_model
from django.core.management import call_command
from django.test import TestCase

from apps.agenda.models import Cita
from apps.clientes.models import Cliente, Mascota
from apps.compras.models import Compra
from apps.facturacion.models import Factura
from apps.grooming.models import CatalogoServicio, ServicioGrooming
from apps.inventario.models import Producto
from apps.medico.models import HistoriaClinica, Vacuna
from apps.telemedicina.models import ConsultaVirtual


class SeedDemoTests(TestCase):
    def test_seed_creates_an_authenticatable_complete_demo(self):
        call_command('seed_demo', verbosity=0)

        user = get_user_model().objects.get(username='admin_demo')
        self.assertTrue(user.check_password(os.environ.get('PETMOR_DEMO_PASSWORD', 'PetmorDemo2026!')))
        self.assertTrue(user.is_superuser)
        self.assertGreaterEqual(Cliente.objects.count(), 3)
        self.assertGreaterEqual(Mascota.objects.count(), 3)
        self.assertGreaterEqual(Cita.objects.count(), 3)
        self.assertTrue(HistoriaClinica.objects.exists())
        self.assertTrue(Vacuna.objects.exists())
        self.assertTrue(ConsultaVirtual.objects.exists())
        self.assertTrue(ServicioGrooming.objects.exists())
        self.assertTrue(Compra.objects.exists())
        self.assertTrue(Factura.objects.exists())
        self.assertGreaterEqual(CatalogoServicio.objects.count(), 2)

    def test_seed_is_idempotent_for_counts_and_stock(self):
        call_command('seed_demo', verbosity=0)
        first_counts = {
            'clientes': Cliente.objects.count(),
            'mascotas': Mascota.objects.count(),
            'citas': Cita.objects.count(),
            'compras': Compra.objects.count(),
            'facturas': Factura.objects.count(),
        }
        first_stock = dict(Producto.objects.values_list('codigo', 'stock_actual'))

        call_command('seed_demo', verbosity=0)

        self.assertEqual(first_counts, {
            'clientes': Cliente.objects.count(),
            'mascotas': Mascota.objects.count(),
            'citas': Cita.objects.count(),
            'compras': Compra.objects.count(),
            'facturas': Factura.objects.count(),
        })
        self.assertEqual(first_stock, dict(Producto.objects.values_list('codigo', 'stock_actual')))
