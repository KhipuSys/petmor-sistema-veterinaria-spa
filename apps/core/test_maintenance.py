import sqlite3
from pathlib import Path
from tempfile import TemporaryDirectory

from django.contrib.auth import get_user_model
from django.test import TestCase, TransactionTestCase
from django.urls import reverse

from apps.clientes.models import Cliente
from apps.core.maintenance import (
    create_sqlite_backup,
    get_database_status,
    reset_business_data,
    validate_sqlite_backup,
)
from apps.core.management.commands.seed_demo import Command as SeedDemoCommand
from apps.grooming.models import CatalogoServicio
from apps.inventario.models import Producto


class MaintenanceServiceTests(TransactionTestCase):
    def setUp(self):
        SeedDemoCommand().handle(verbosity=0)

    def test_database_status_uses_sqlite_introspection(self):
        with TemporaryDirectory() as directory:
            status = get_database_status(Path(directory))

        self.assertGreater(status['total_registros'], 0)
        self.assertTrue(any(item['nombre'] == 'clientes_cliente' for item in status['tablas_info']))
        self.assertEqual(status['archivos_backup'], 0)

    def test_backup_is_a_valid_sqlite_database(self):
        with TemporaryDirectory() as directory:
            path = create_sqlite_backup(Path(directory))
            validate_sqlite_backup(path)

            self.assertEqual(path.read_bytes()[:16], b'SQLite format 3\x00')

    def test_invalid_backup_is_rejected(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'falso.sqlite3'
            path.write_text('esto no es sqlite', encoding='utf-8')

            with self.assertRaises(ValueError):
                validate_sqlite_backup(path)

    def test_incomplete_sqlite_schema_is_rejected(self):
        with TemporaryDirectory() as directory:
            path = Path(directory) / 'incompleto.sqlite3'
            database = sqlite3.connect(path)
            try:
                database.execute('CREATE TABLE django_migrations (app text, name text)')
                database.execute('CREATE TABLE auth_user (id integer)')
                database.commit()
            finally:
                database.close()

            with self.assertRaises(ValueError):
                validate_sqlite_backup(path)

    def test_reset_removes_operations_but_keeps_catalogs_and_users(self):
        products = Producto.objects.count()
        original_stock = dict(Producto.objects.values_list('codigo', 'stock_actual'))
        services = CatalogoServicio.objects.count()
        users = get_user_model().objects.count()

        reset_business_data()

        self.assertEqual(Cliente.objects.count(), 0)
        self.assertEqual(Producto.objects.count(), products)
        self.assertFalse(Producto.objects.exclude(stock_actual=0).exists())
        self.assertEqual(CatalogoServicio.objects.count(), services)
        self.assertEqual(get_user_model().objects.count(), users)

        SeedDemoCommand().handle(verbosity=0)
        self.assertEqual(original_stock, dict(Producto.objects.values_list('codigo', 'stock_actual')))


class MaintenanceViewTests(TestCase):
    def setUp(self):
        User = get_user_model()
        self.admin = User.objects.create_superuser('admin', password='PruebaLocal123!')
        self.regular = User.objects.create_user('regular', password='PruebaLocal123!')

    def test_admin_can_open_sqlite_maintenance_page(self):
        self.client.force_login(self.admin)

        response = self.client.get(reverse('mantenimiento_index'))

        self.assertEqual(response.status_code, 200)
        self.assertContains(response, 'SQLite')

    def test_regular_user_cannot_open_maintenance_page(self):
        self.client.force_login(self.regular)

        response = self.client.get(reverse('mantenimiento_index'))

        self.assertRedirects(response, reverse('dashboard'))
