import os
import uuid
from datetime import time, timedelta
from decimal import Decimal

from django.contrib.auth import get_user_model
from django.core.management.base import BaseCommand
from django.db import transaction
from django.utils import timezone

from apps.agenda.models import Cita
from apps.clientes.models import Cliente, Mascota
from apps.compras.models import Compra
from apps.compras.services import recibir_compra
from apps.configuracion.models import Configuracion
from apps.facturacion.services import registrar_venta
from apps.grooming.models import CatalogoServicio, ConsumoInsumo, ServicioGrooming
from apps.grooming.services import consumir_insumo
from apps.inventario.models import Categoria, Producto, Proveedor
from apps.medico.models import HistoriaClinica, Vacuna
from apps.telemedicina.models import ConsultaVirtual


DEMO_SALE_KEY = uuid.UUID('d673bb7d-5c8a-45f5-b84b-2fce8db809b1')
DEMO_CONSUMPTION_KEY = uuid.UUID('e36a9038-dada-45fb-a821-0b2f25cd3802')


class Command(BaseCommand):
    help = 'Crea o actualiza datos ficticios para la demostracion local de Petmor.'

    @transaction.atomic
    def handle(self, *args, **options):
        today = timezone.localdate()
        password = os.environ.get('PETMOR_DEMO_PASSWORD', 'PetmorDemo2026!')
        User = get_user_model()

        admin, _ = User.objects.update_or_create(
            username='admin_demo',
            defaults={
                'first_name': 'Administrador',
                'last_name': 'Petmor Demo',
                'email': 'admin@petmor.example',
                'is_staff': True,
                'is_superuser': True,
                'is_active': True,
            },
        )
        admin.set_password(password)
        admin.save(update_fields=['password'])
        veterinarian, _ = User.objects.update_or_create(
            username='veterinario_demo',
            defaults={
                'first_name': 'Valeria',
                'last_name': 'Demo',
                'email': 'veterinaria@petmor.example',
                'is_staff': True,
                'is_superuser': False,
                'is_active': True,
            },
        )
        veterinarian.set_password(password)
        veterinarian.save(update_fields=['password'])

        Configuracion.objects.update_or_create(
            pk=1,
            defaults={
                'nombre_empresa': 'Veterinaria Petmor',
                'slogan': 'Salud, cuidado y bienestar animal',
                'simbolo_moneda': 'S/',
                'nombre_moneda': 'Soles',
                'ruc': '20000000001',
                'telefono': '999 000 001',
                'email': 'contacto@petmor.example',
                'direccion': 'Av. Demostracion 123',
                'ciudad': 'Lima',
                'pais': 'Peru',
                'color_primario': '#0bb8c8',
            },
        )

        categories = {}
        for name, description in (
            ('Vacunas Demo', 'Vacunas de uso veterinario para la demostracion.'),
            ('Farmacia Demo', 'Medicamentos y accesorios ficticios.'),
            ('Insumos Spa Demo', 'Insumos de peluqueria ficticios.'),
        ):
            categories[name], _ = Categoria.objects.update_or_create(
                nombre=name, defaults={'descripcion': description}
            )

        providers = {}
        for ruc, name in (
            ('20000000002', 'Distribuidora Veterinaria Demo'),
            ('20000000003', 'Insumos Pet Demo'),
        ):
            providers[ruc], _ = Proveedor.objects.update_or_create(
                ruc=ruc,
                defaults={
                    'nombre': name,
                    'telefono': '999 000 002',
                    'email': f'{ruc}@proveedor.example',
                    'direccion': 'Direccion ficticia para demostracion',
                    'contacto': 'Contacto Demo',
                },
            )

        products = {}
        product_specs = (
            ('VAC-DEMO-01', 'Vacuna Triple Canina Demo', 'Vacunas Demo', '20000000002', 'VETERINARIA', 'dosis', '25.00', '45.00', 5),
            ('MED-DEMO-01', 'Antiparasitario Demo', 'Farmacia Demo', '20000000002', 'VETERINARIA', 'unidad', '12.00', '24.00', 5),
            ('SPA-DEMO-01', 'Shampoo Hipoalergenico Demo', 'Insumos Spa Demo', '20000000003', 'SPA', 'ml', '0.08', '0.20', 500),
            ('ACC-DEMO-01', 'Collar Reflectante Demo', 'Farmacia Demo', '20000000003', 'GENERAL', 'unidad', '10.00', '22.00', 4),
        )
        for code, name, category, provider, area, unit, buy, sell, minimum in product_specs:
            product, created = Producto.objects.get_or_create(
                codigo=code,
                defaults={
                    'nombre': name,
                    'categoria': categories[category],
                    'proveedor': providers[provider],
                    'descripcion': 'Registro ficticio para exposicion.',
                    'area': area,
                    'unidad': unit,
                    'precio_compra': Decimal(buy),
                    'precio_venta': Decimal(sell),
                    'stock_actual': 0,
                    'stock_minimo': minimum,
                    'is_active': True,
                },
            )
            if not created:
                product.nombre = name
                product.categoria = categories[category]
                product.proveedor = providers[provider]
                product.area = area
                product.unidad = unit
                product.precio_venta = Decimal(sell)
                product.stock_minimo = minimum
                product.is_active = True
                product.save(update_fields=[
                    'nombre', 'categoria', 'proveedor', 'area', 'unidad',
                    'precio_venta', 'stock_minimo', 'is_active',
                ])
            products[code] = product

        clients = {}
        client_specs = (
            ('00000001', 'Ana', 'Demostracion', '900000001'),
            ('00000002', 'Bruno', 'Ejemplo', '900000002'),
            ('00000003', 'Carla', 'Prueba', '900000003'),
        )
        for dni, names, surnames, phone in client_specs:
            clients[dni], _ = Cliente.objects.update_or_create(
                dni=dni,
                defaults={
                    'nombres': names,
                    'apellidos': surnames,
                    'telefono': phone,
                    'email': f'{dni}@cliente.example',
                    'direccion': 'Direccion ficticia',
                    'is_active': True,
                },
            )

        pet_specs = (
            ('Luna Demo', '00000001', 'CANINO', 'Mestiza', 'H', Decimal('8.50'), 'Caramelo'),
            ('Milo Demo', '00000002', 'FELINO', 'Comun europeo', 'M', Decimal('4.20'), 'Gris'),
            ('Rocky Demo', '00000003', 'CANINO', 'Labrador', 'M', Decimal('24.00'), 'Dorado'),
        )
        pets = {}
        for name, dni, species, breed, sex, weight, color in pet_specs:
            pet, _ = Mascota.objects.update_or_create(
                cliente=clients[dni],
                nombre=name,
                defaults={
                    'especie': species,
                    'raza': breed,
                    'sexo': sex,
                    'fecha_nacimiento': today - timedelta(days=365 * 3),
                    'peso_actual': weight,
                    'color': color,
                    'is_active': True,
                },
            )
            pets[name] = pet

        consultation, _ = Cita.objects.update_or_create(
            mascota=pets['Luna Demo'], motivo='[DEMO] Control general',
            defaults={
                'veterinario': veterinarian, 'fecha': today, 'hora': time(9, 0),
                'tipo': 'CONSULTA', 'estado': 'COMPLETADA',
                'observaciones': 'Cita ficticia para exposicion.', 'is_active': True,
            },
        )
        virtual_appointment, _ = Cita.objects.update_or_create(
            mascota=pets['Milo Demo'], motivo='[DEMO] Orientacion virtual',
            defaults={
                'veterinario': veterinarian, 'fecha': today + timedelta(days=1), 'hora': time(11, 0),
                'tipo': 'CONSULTA', 'estado': 'PENDIENTE', 'is_active': True,
            },
        )
        grooming_appointment, _ = Cita.objects.update_or_create(
            mascota=pets['Rocky Demo'], motivo='[DEMO] Bano y corte',
            defaults={
                'veterinario': veterinarian, 'fecha': today, 'hora': time(15, 0),
                'tipo': 'GROOMING', 'estado': 'EN_ATENCION', 'is_active': True,
            },
        )

        HistoriaClinica.objects.update_or_create(
            cita=consultation,
            defaults={
                'mascota': pets['Luna Demo'], 'veterinario': veterinarian,
                'motivo_consulta': 'Control preventivo de demostracion.',
                'anamnesis': 'Paciente ficticio sin antecedentes relevantes.',
                'peso': Decimal('8.50'), 'temperatura': Decimal('38.5'),
                'frecuencia_cardiaca': 90, 'frecuencia_respiratoria': 24,
                'mucosas': 'Rosadas', 'tiempo_llenado_capilar': '2 segundos',
                'diagnostico': 'Paciente clinicamente estable.',
                'tratamiento': 'Continuar cuidados preventivos.',
                'proxima_cita': today + timedelta(days=30),
            },
        )
        Vacuna.objects.update_or_create(
            mascota=pets['Luna Demo'], nombre_vacuna='Triple Canina Demo',
            defaults={
                'producto': products['VAC-DEMO-01'], 'lote': 'LOTE-DEMO-01',
                'fecha_aplicacion': today, 'fecha_proxima_dosis': today + timedelta(days=365),
                'veterinario': veterinarian, 'observaciones': 'Registro ficticio.',
            },
        )
        ConsultaVirtual.objects.update_or_create(
            cita=virtual_appointment,
            defaults={
                'plataforma': 'MEET',
                'enlace_reunion': 'https://teleconsulta.invalid/petmor-demo',
                'codigo_acceso': 'DEMO', 'estado_conexion': 'PROGRAMADA',
                'notas_preliminares': 'Consulta ficticia para exposicion.',
            },
        )

        bath_service, _ = CatalogoServicio.objects.update_or_create(
            nombre='Bano completo Demo',
            defaults={'area': 'SPA', 'descripcion': 'Bano, secado y cepillado.', 'precio': Decimal('50.00'), 'duracion_minutos': 60, 'activo': True},
        )
        CatalogoServicio.objects.update_or_create(
            nombre='Consulta preventiva Demo',
            defaults={'area': 'VETERINARIA', 'descripcion': 'Evaluacion veterinaria general.', 'precio': Decimal('60.00'), 'duracion_minutos': 30, 'activo': True},
        )
        grooming_order, _ = ServicioGrooming.objects.update_or_create(
            cita=grooming_appointment,
            defaults={
                'servicio': bath_service, 'peluquero': veterinarian, 'bano': True,
                'corte_pelo': True, 'corte_unas': True, 'limpieza_oidos': True,
                'perfume': True, 'observaciones': 'Orden ficticia para exposicion.',
                'fecha_inicio': timezone.now(), 'is_active': True,
            },
        )

        if not Compra.objects.filter(proveedor=providers['20000000002'], comprobante='COMPRA-DEMO-001').exists():
            recibir_compra(
                proveedor=providers['20000000002'], comprobante='COMPRA-DEMO-001',
                fecha=today, observaciones='Compra ficticia para demostracion.', usuario=admin,
                lineas=[
                    {'producto': products['VAC-DEMO-01'], 'cantidad': 20, 'costo_unitario': Decimal('25.00')},
                    {'producto': products['MED-DEMO-01'], 'cantidad': 30, 'costo_unitario': Decimal('12.00')},
                ],
            )
        if not Compra.objects.filter(proveedor=providers['20000000003'], comprobante='COMPRA-DEMO-002').exists():
            recibir_compra(
                proveedor=providers['20000000003'], comprobante='COMPRA-DEMO-002',
                fecha=today, observaciones='Compra ficticia de spa.', usuario=admin,
                lineas=[
                    {'producto': products['SPA-DEMO-01'], 'cantidad': 3000, 'costo_unitario': Decimal('0.08')},
                    {'producto': products['ACC-DEMO-01'], 'cantidad': 15, 'costo_unitario': Decimal('10.00')},
                ],
            )

        registrar_venta(
            cliente=clients['00000001'], metodo_pago='EFECTIVO', usuario=admin,
            clave=DEMO_SALE_KEY,
            lineas=[{
                'producto_id': str(products['MED-DEMO-01'].pk), 'cantidad': 1,
                'precio': '24.00', 'descripcion': products['MED-DEMO-01'].nombre,
            }],
        )
        if not ConsumoInsumo.objects.filter(clave=DEMO_CONSUMPTION_KEY).exists():
            consumir_insumo(
                orden=grooming_order, producto=products['SPA-DEMO-01'], cantidad=40,
                clave=DEMO_CONSUMPTION_KEY, usuario=admin,
            )

        self.stdout.write(self.style.SUCCESS('Datos de demostracion listos.'))
