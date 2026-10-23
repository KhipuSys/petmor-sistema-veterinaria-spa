import uuid
import json
from datetime import time
from decimal import Decimal
from unittest.mock import patch, MagicMock
from django.contrib.auth import get_user_model
from django.core.exceptions import ValidationError
from django.db import IntegrityError, transaction
from django.test import TestCase, override_settings
from django.urls import reverse
from django.utils import timezone
from apps.clientes.models import Cliente, Mascota
from apps.agenda.models import Cita
from apps.inventario.models import Producto, Proveedor, Movimiento
from apps.inventario.services import registrar_movimiento
from apps.facturacion.models import Factura, Caja
from apps.facturacion.services import registrar_venta, anular_venta
from apps.grooming.models import ServicioGrooming, CatalogoServicio
from apps.grooming.services import consumir_insumo
from apps.integraciones.factiliza import consultar_documento, FactilizaError
from .models import Compra
from .services import recibir_compra, anular_compra


class OperacionERPTests(TestCase):
    def setUp(self):
        self.user = get_user_model().objects.create_user('erp', password='Prueba-local-123', is_superuser=True, is_staff=True)
        self.proveedor = Proveedor.objects.create(nombre='Distribuidor')
        self.producto = Producto.objects.create(nombre='Champú', precio_compra=2, precio_venta=3, area='SPA')
        cliente = Cliente.objects.create(nombres='Ana', apellidos='Pérez', dni='12345678', telefono='999999999')
        mascota = Mascota.objects.create(cliente=cliente, nombre='Luna', especie='CANINO', sexo='H')
        cita = Cita.objects.create(mascota=mascota, fecha=timezone.localdate(), hora=time(10), tipo='GROOMING')
        self.orden = ServicioGrooming.objects.create(cita=cita)
        self.client.force_login(self.user)

    def compra(self, cantidad=10, comprobante='F001-1'):
        return recibir_compra(proveedor=self.proveedor, comprobante=comprobante, fecha=timezone.localdate(),
                              observaciones='', usuario=self.user,
                              lineas=[{'producto': self.producto, 'cantidad': cantidad, 'costo_unitario': Decimal('2.00')}])

    def venta(self, cantidad=2, clave=None, producto=None):
        return registrar_venta(cliente=None, metodo_pago='EFECTIVO', usuario=self.user, clave=clave or uuid.uuid4(),
                               lineas=[{'descripcion': 'Champú', 'cantidad': str(cantidad), 'precio': '3.00',
                                        'producto_id': producto or str(self.producto.pk)}])

    def test_compra_venta_anulacion_concilian_stock_y_caja(self):
        self.compra()
        venta = self.venta()
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 8)
        self.assertEqual(venta.total, Decimal('7.08'))
        self.assertEqual(Caja.objects.get().total_ingresos, venta.total)
        anular_venta(venta.pk, self.user)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 10)
        self.assertEqual(Caja.objects.get().total_ingresos, 0)
        self.assertEqual(list(Movimiento.objects.order_by('pk').values_list('saldo_posterior', flat=True)), [10, 8, 10])
        with self.assertRaises(ValidationError):
            anular_venta(venta.pk, self.user)

    def test_venta_insuficiente_no_deja_registros_parciales(self):
        self.compra(1)
        with self.assertRaises(ValidationError):
            self.venta(2)
        self.assertFalse(Factura.objects.exists())
        self.assertFalse(Caja.objects.exists())
        self.assertEqual(Movimiento.objects.count(), 1)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 1)

    def test_compra_invalida_revierte_todas_las_lineas(self):
        with self.assertRaises(ValidationError):
            recibir_compra(proveedor=self.proveedor, comprobante='fallo', fecha=timezone.localdate(),
                           observaciones='', usuario=self.user, lineas=[
                               {'producto': self.producto, 'cantidad': 5, 'costo_unitario': Decimal('2')},
                               {'producto': self.producto, 'cantidad': -1, 'costo_unitario': Decimal('2')},
                           ])
        self.assertFalse(Compra.objects.exists())
        self.assertFalse(Movimiento.objects.exists())
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 0)

    def test_compra_duplicada_no_duplica_stock(self):
        self.compra()
        with self.assertRaises(IntegrityError), transaction.atomic():
            self.compra()
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 10)

    def test_anular_compra_valida_stock(self):
        compra = self.compra()
        self.venta()
        with self.assertRaises(ValidationError):
            anular_compra(compra.pk, self.user)
        compra.refresh_from_db()
        self.assertEqual(compra.estado, 'RECIBIDA')
        anular_venta(Factura.objects.get().pk, self.user)
        anular_compra(compra.pk, self.user)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 0)

    def test_consumo_spa_descontado_una_sola_vez(self):
        self.compra()
        clave = uuid.uuid4()
        consumo = consumir_insumo(orden=self.orden, producto=self.producto, cantidad=3, clave=clave, usuario=self.user)
        self.assertEqual(consumo.costo_total, Decimal('6.00'))
        with self.assertRaises(ValidationError):
            consumir_insumo(orden=self.orden, producto=self.producto, cantidad=3, clave=clave, usuario=self.user)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 7)

    def test_ajuste_fija_conteo_y_admite_cero(self):
        self.compra()
        registrar_movimiento(producto=self.producto, tipo='AJUSTE', cantidad=4, motivo='Conteo', usuario=self.user)
        registrar_movimiento(producto=self.producto, tipo='AJUSTE', cantidad=0, motivo='Conteo', usuario=self.user)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 0)
        self.assertEqual(Movimiento.objects.latest('pk').saldo_anterior, 4)

    def test_reenvio_venta_no_duplica(self):
        self.compra()
        clave = uuid.uuid4()
        primera = self.venta(clave=clave)
        self.assertEqual(self.venta(clave=clave).pk, primera.pk)
        self.assertEqual(Factura.objects.count(), 1)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 8)

    def test_caja_cerrada_impide_venta(self):
        self.compra()
        Caja.objects.create(cerrada=True)
        with self.assertRaises(ValidationError):
            self.venta()
        self.assertFalse(Factura.objects.exists())

    def test_servicio_no_descontado_como_producto(self):
        servicio = CatalogoServicio.objects.create(nombre='Baño', precio=30)
        factura = self.venta(producto=f's:{servicio.pk}')
        self.assertEqual(factura.detalles.get().servicio, servicio)
        self.assertFalse(Movimiento.objects.exists())

    def test_paginas_erp_y_formularios(self):
        nombres = ['dashboard', 'proveedor_lista', 'proveedor_crear', 'compra_lista', 'compra_crear',
                   'producto_lista', 'producto_crear', 'categoria_lista', 'categoria_crear', 'movimiento_lista',
                   'movimiento_crear', 'grooming_lista', 'grooming_crear', 'servicio_catalogo', 'servicio_crear',
                   'factura_crear', 'factura_lista', 'caja_hoy', 'integraciones_estado']
        for nombre in nombres:
            with self.subTest(nombre=nombre):
                self.assertEqual(self.client.get(reverse(nombre)).status_code, 200)
        self.assertEqual(self.client.get(reverse('grooming_consumos', args=[self.orden.pk])).status_code, 200)
        compra = self.compra()
        self.assertEqual(self.client.get(reverse('compra_detalle', args=[compra.pk])).status_code, 200)

    def test_rutas_independientes(self):
        self.assertEqual(reverse('proveedor_lista'), '/proveedores/')
        self.assertEqual(reverse('movimiento_lista'), '/kardex/')
        self.client.logout()
        self.assertEqual(self.client.get('/compras/').status_code, 302)
        self.assertEqual(self.client.post('/integraciones/factiliza/documento/', {'documento': '12345678'}).status_code, 302)

    def test_post_compra_y_validacion_de_error(self):
        data = {'proveedor': self.proveedor.pk, 'comprobante': 'POST-1', 'fecha': timezone.localdate().isoformat(),
                'lineas-TOTAL_FORMS': '1', 'lineas-INITIAL_FORMS': '0', 'lineas-0-producto': self.producto.pk,
                'lineas-0-cantidad': '5', 'lineas-0-costo_unitario': '2.00'}
        response = self.client.post(reverse('compra_crear'), data)
        self.assertEqual(response.status_code, 302)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 5)
        response = self.client.post(reverse('compra_crear'), data)
        self.assertEqual(response.status_code, 200)
        self.assertEqual(Compra.objects.count(), 1)

    def test_post_venta_y_reenvio(self):
        self.compra()
        data = {'clave_operacion': str(uuid.uuid4()), 'metodo_pago': 'EFECTIVO',
                'descripcion[]': ['Champú'], 'cantidad[]': ['2'], 'precio[]': ['3.00'],
                'producto_id[]': [str(self.producto.pk)]}
        for _ in range(2):
            self.assertEqual(self.client.post(reverse('factura_crear'), data).status_code, 302)
        self.assertEqual(Factura.objects.count(), 1)
        self.producto.refresh_from_db()
        self.assertEqual(self.producto.stock_actual, 8)

    def test_post_movimiento_insuficiente_muestra_error(self):
        response = self.client.post(reverse('movimiento_crear'), {
            'producto': self.producto.pk, 'tipo': 'SALIDA', 'cantidad': '3', 'motivo': 'Consumo',
        })
        self.assertContains(response, 'Stock insuficiente')
        self.assertFalse(Movimiento.objects.exists())

    @override_settings(FACTILIZA_TOKEN='')
    def test_consulta_sin_token_permite_seguir_manual(self):
        response = self.client.post(reverse('factiliza_documento'), {'documento': '12345678'})
        self.assertEqual(response.status_code, 400)
        self.assertFalse(response.json()['ok'])
        self.assertIn('manualmente', response.json()['mensaje'])

    def test_caja_usa_fecha_peru_al_cruzar_medianoche_utc(self):
        from datetime import datetime, timezone as utc
        with patch('django.utils.timezone.now', return_value=datetime(2026, 9, 23, 2, tzinfo=utc.utc)):
            caja = Caja.objects.create()
            self.assertEqual(str(caja.fecha), '2026-09-22')


class FactilizaTests(TestCase):
    @override_settings(FACTILIZA_TOKEN='')
    @patch('apps.integraciones.factiliza.urlopen')
    def test_sin_token_no_hace_solicitud(self, opener):
        with self.assertRaises(FactilizaError):
            consultar_documento('12345678')
        opener.assert_not_called()

    @override_settings(FACTILIZA_TOKEN='token-de-prueba')
    @patch('apps.integraciones.factiliza.urlopen')
    def test_consulta_dni_normalizada(self, opener):
        response = MagicMock()
        response.read.return_value = json.dumps({'data': {'nombres': 'Ana', 'apellido_paterno': 'Perez', 'apellido_materno': 'Diaz'}}).encode()
        opener.return_value.__enter__.return_value = response
        data = consultar_documento('12345678')
        self.assertEqual(data['nombre'], 'Ana Perez Diaz')
        self.assertEqual(data['apellidos'], 'Perez Diaz')
        self.assertEqual(opener.call_args.args[0].full_url, 'https://api.factiliza.com/v1/dni/info/12345678')
        self.assertEqual(opener.call_args.kwargs['timeout'], 10)

    @override_settings(FACTILIZA_TOKEN='token-de-prueba')
    @patch('apps.integraciones.factiliza.urlopen', side_effect=TimeoutError)
    def test_timeout_controlado(self, opener):
        with self.assertRaises(FactilizaError):
            consultar_documento('12345678')

    @patch('apps.integraciones.factiliza.urlopen')
    def test_documento_invalido_no_hace_solicitud(self, opener):
        with self.assertRaises(FactilizaError):
            consultar_documento('../123')
        opener.assert_not_called()
