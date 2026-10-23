import os
import sqlite3
import tempfile
from datetime import datetime
from pathlib import Path

from django.conf import settings
from django.db import connection, connections, transaction


MAX_BACKUP_SIZE = 100 * 1024 * 1024
REQUIRED_TABLES = {
    'django_migrations', 'django_session', 'auth_user',
    'clientes_cliente', 'clientes_mascota', 'agenda_cita',
    'medico_historiaclinica', 'medico_vacuna',
    'inventario_producto', 'inventario_movimiento',
    'compras_compra', 'compras_detallecompra',
    'facturacion_factura', 'facturacion_caja',
    'grooming_catalogoservicio', 'grooming_serviciogrooming',
    'telemedicina_consultavirtual', 'configuracion_configuracion',
}


def get_database_status(backups_dir=None):
    backups_dir = Path(backups_dir or settings.BASE_DIR / 'backups')
    tables = [
        name for name in connection.introspection.table_names()
        if not name.startswith(('auth_', 'django_', 'sqlite_'))
    ]
    table_info = []
    total = 0
    with connection.cursor() as cursor:
        for name in sorted(tables):
            cursor.execute(f'SELECT COUNT(*) FROM {connection.ops.quote_name(name)}')
            count = cursor.fetchone()[0]
            table_info.append({'nombre': name, 'cantidad': count})
            total += count

    database_path = Path(settings.DATABASES['default']['NAME'])
    size_mb = round(database_path.stat().st_size / 1024 / 1024, 2) if database_path.exists() else 0
    backup_count = len(list(backups_dir.glob('*.sqlite3'))) if backups_dir.exists() else 0
    return {
        'tablas_info': table_info,
        'total_registros': total,
        'tamano_bd_mb': size_mb,
        'archivos_backup': backup_count,
    }


def create_sqlite_backup(backups_dir=None):
    backups_dir = Path(backups_dir or settings.BASE_DIR / 'backups')
    backups_dir.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    destination = backups_dir / f'backup_petmor_{timestamp}.sqlite3'

    connection.ensure_connection()
    target = sqlite3.connect(destination)
    try:
        connection.connection.backup(target)
    finally:
        target.close()
    validate_sqlite_backup(destination)
    return destination


def validate_sqlite_backup(path):
    path = Path(path)
    if not path.is_file() or path.stat().st_size > MAX_BACKUP_SIZE:
        raise ValueError('El respaldo no existe o excede 100 MB.')
    if path.read_bytes()[:16] != b'SQLite format 3\x00':
        raise ValueError('El archivo no es una base SQLite valida.')

    database = None
    try:
        database = sqlite3.connect(f'file:{path.as_posix()}?mode=ro', uri=True)
        try:
            result = database.execute('PRAGMA integrity_check').fetchone()
            if not result or result[0] != 'ok':
                raise ValueError('La verificacion de integridad del respaldo fallo.')
            tables = {
                row[0] for row in database.execute(
                    "SELECT name FROM sqlite_master WHERE type='table'"
                )
            }
            applied_migrations = {
                (row[0], row[1])
                for row in database.execute('SELECT app, name FROM django_migrations')
            }
        finally:
            database.close()
    except sqlite3.DatabaseError as error:
        raise ValueError('El respaldo SQLite esta danado.') from error

    if not REQUIRED_TABLES.issubset(tables):
        raise ValueError('El archivo no corresponde a una instalacion de Petmor.')

    from django.db.migrations.loader import MigrationLoader
    required_migrations = set(MigrationLoader(connection, ignore_no_migrations=True).disk_migrations)
    if not required_migrations.issubset(applied_migrations):
        raise ValueError('El respaldo pertenece a una version incompatible de Petmor.')
    return True


def restore_sqlite_backup(uploaded_file):
    if uploaded_file.size > MAX_BACKUP_SIZE:
        raise ValueError('El respaldo excede el limite de 100 MB.')

    backups_dir = Path(settings.BASE_DIR) / 'backups'
    backups_dir.mkdir(parents=True, exist_ok=True)
    descriptor, temp_name = tempfile.mkstemp(suffix='.sqlite3', dir=backups_dir)
    os.close(descriptor)
    temporary = Path(temp_name)
    try:
        with temporary.open('wb') as destination:
            for chunk in uploaded_file.chunks():
                destination.write(chunk)
        validate_sqlite_backup(temporary)
        create_sqlite_backup(backups_dir)
        database_path = Path(settings.DATABASES['default']['NAME'])
        connections.close_all()
        os.replace(temporary, database_path)
    finally:
        temporary.unlink(missing_ok=True)


@transaction.atomic
def reset_business_data():
    from apps.agenda.models import Cita
    from apps.clientes.models import Cliente, Mascota
    from apps.compras.models import Compra, DetalleCompra
    from apps.facturacion.models import Caja, DetalleFactura, Factura
    from apps.grooming.models import ConsumoInsumo, ServicioGrooming
    from apps.inventario.models import Movimiento, Producto
    from apps.medico.models import DetalleReceta, HistoriaClinica, Receta, Vacuna
    from apps.telemedicina.models import ConsultaVirtual

    for model in (
        ConsumoInsumo,
        DetalleFactura,
        Factura,
        DetalleCompra,
        Compra,
        ConsultaVirtual,
        DetalleReceta,
        Receta,
        Vacuna,
        HistoriaClinica,
        ServicioGrooming,
        Movimiento,
        Cita,
        Mascota,
        Cliente,
        Caja,
    ):
        model.objects.all().delete()
    Producto.objects.update(stock_actual=0)
