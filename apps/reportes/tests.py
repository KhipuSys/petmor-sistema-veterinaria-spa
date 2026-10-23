from django.test import SimpleTestCase

from .views import excel_safe_text


class ExcelSafetyTests(SimpleTestCase):
    def test_formula_prefixes_are_exported_as_plain_text(self):
        for value in ('=1+1', '+SUM(A1:A2)', '-2+3', '@IMPORTDATA("x")'):
            with self.subTest(value=value):
                self.assertEqual(excel_safe_text(value), "'" + value)

    def test_regular_text_is_unchanged(self):
        self.assertEqual(excel_safe_text('Cliente Demo'), 'Cliente Demo')

# Create your tests here.
