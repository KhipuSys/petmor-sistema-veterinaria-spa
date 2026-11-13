const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');

const baseUrl = process.env.QA_BASE_URL || 'http://127.0.0.1:5203';
const username = process.env.QA_USERNAME;
const password = process.env.QA_PASSWORD;
const evidenceDir = path.resolve(
  process.env.QA_EVIDENCE_DIR || path.join(__dirname, '../../Docs/QA/rfc-004-accessibility')
);

if (!['127.0.0.1', 'localhost'].includes(new URL(baseUrl).hostname)) {
  throw new Error('La prueba solo permite un servidor local.');
}
if (!username || !password) {
  throw new Error('Define QA_USERNAME y QA_PASSWORD para iniciar sesión en Petmor.');
}

async function main() {
  fs.mkdirSync(evidenceDir, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ viewport: { width: 1365, height: 900 } });
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));

  try {
    await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: 'domcontentloaded' });
    await page.locator('#Input_Email').fill(username);
    await page.locator('#Input_Password').fill(password);
    await page.locator('button[type="submit"]').click();
    await page.waitForURL(url => !url.pathname.includes('/Identity/Account/Login'));
    await page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded' });

    const toggle = page.locator('#accessibility-toggle-btn');
    if (!(await toggle.isVisible())) throw new Error('No se ve el botón de accesibilidad.');
    if (!(await toggle.getAttribute('aria-label'))) throw new Error('Al botón le falta nombre accesible.');
    if ((await toggle.getAttribute('aria-expanded')) !== 'false') throw new Error('El estado inicial del botón debe ser contraído.');
    if ((await toggle.getAttribute('aria-controls')) !== 'accessibility-panel') throw new Error('El botón no identifica el panel que controla.');

    await toggle.focus();
    await page.keyboard.press('Enter');
    const panel = page.locator('#accessibility-panel');
    await panel.waitFor({ state: 'visible' });
    if ((await panel.getAttribute('role')) !== 'dialog') throw new Error('El panel debe tener semántica de diálogo.');
    if ((await panel.getAttribute('aria-hidden')) !== 'false' || (await toggle.getAttribute('aria-expanded')) !== 'true') {
      throw new Error('El estado abierto no se comunica correctamente a tecnología de asistencia.');
    }
    if ((await page.evaluate(() => document.activeElement?.id)) !== 'accessibility-close-btn') {
      throw new Error('Al abrir el panel, el foco debe pasar al botón para cerrarlo.');
    }

    for (const [action, label] of [
      ['text-size', 'Aumentar tamaño del texto'],
      ['contrast', 'Cambiar contraste'],
      ['cursor', 'Alternar cursor grande'],
      ['dyslexia', 'Alternar fuente amigable'],
      ['reset', 'Restablecer preferencias de accesibilidad'],
    ]) {
      const control = page.locator(`[data-accessibility-action="${action}"]`);
      if (!(await control.isVisible())) throw new Error(`No está visible la opción de accesibilidad ${action}.`);
      if (await control.getAttribute('role') !== 'button' && await control.evaluate(element => element.tagName) !== 'BUTTON') {
        throw new Error(`La opción ${action} no es un control de botón accesible.`);
      }
      if (!(await control.getAttribute('aria-label')) || !(await control.getAttribute('aria-label')).includes(label)) {
        throw new Error(`La opción ${action} no tiene el nombre accesible esperado.`);
      }
      if ((await control.getAttribute('tabindex')) === '-1') throw new Error(`La opción ${action} no es alcanzable con Tab.`);
    }

    const panelBounds = await panel.boundingBox();
    if (!panelBounds || panelBounds.x < 0 || panelBounds.x + panelBounds.width > 1365 || panelBounds.y + panelBounds.height > 900) {
      throw new Error(`El panel se sale del viewport de escritorio: ${JSON.stringify(panelBounds)}.`);
    }
    await page.screenshot({ path: path.join(evidenceDir, 'accessibility-panel-desktop.png'), fullPage: true,
      style: '#userMenu .fw-bold { visibility: hidden !important; }' });

    const textSize = page.locator('[data-accessibility-action="text-size"]');
    await textSize.focus(); await page.keyboard.press('Space');
    if (!(await textSize.getAttribute('aria-pressed') === 'true')) throw new Error('El tamaño de texto no actualiza su estado accesible.');

    const contrast = page.locator('[data-accessibility-action="contrast"]');
    await contrast.focus(); await page.keyboard.press('Enter');
    if (await page.locator('body').getAttribute('data-contrast') !== 'dark' || await contrast.getAttribute('aria-pressed') !== 'true') {
      throw new Error('La opción de contraste no se puede activar con teclado o no anuncia su estado.');
    }

    const cursor = page.locator('[data-accessibility-action="cursor"]');
    await cursor.focus(); await page.keyboard.press('Space');
    if (await cursor.getAttribute('aria-pressed') !== 'true') throw new Error('La opción de cursor no se puede activar con teclado.');

    const dyslexia = page.locator('[data-accessibility-action="dyslexia"]');
    await dyslexia.focus(); await page.keyboard.press('Enter');
    if (await dyslexia.getAttribute('aria-pressed') !== 'true') throw new Error('La opción de fuente no se puede activar con teclado.');

    const reset = page.locator('[data-accessibility-action="reset"]');
    await reset.focus(); await page.keyboard.press('Space');
    for (const action of ['text-size', 'contrast', 'cursor', 'dyslexia']) {
      if (await page.locator(`[data-accessibility-action="${action}"]`).getAttribute('aria-pressed') !== 'false') {
        throw new Error(`Restablecer no limpió el estado accesible de ${action}.`);
      }
    }

    await page.keyboard.press('Escape');
    await panel.waitFor({ state: 'hidden' });
    if ((await toggle.getAttribute('aria-expanded')) !== 'false' ||
        (await page.evaluate(() => document.activeElement?.id)) !== 'accessibility-toggle-btn') {
      throw new Error('Escape debe cerrar el panel y devolver el foco al activador.');
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await toggle.click();
    await panel.waitFor({ state: 'visible' });
    const mobileBounds = await panel.boundingBox();
    if (!mobileBounds || mobileBounds.x < 0 || mobileBounds.x + mobileBounds.width > 390 || mobileBounds.y + mobileBounds.height > 844) {
      throw new Error(`El panel se sale del viewport móvil: ${JSON.stringify(mobileBounds)}.`);
    }
    await page.screenshot({ path: path.join(evidenceDir, 'accessibility-panel-mobile.png'), fullPage: true,
      style: '#userMenu .fw-bold { visibility: hidden !important; }' });

    await page.locator('#accessibility-close-btn').click();
    await panel.waitFor({ state: 'hidden' });
    if (pageErrors.length) throw new Error(`Errores JavaScript: ${pageErrors.join(' | ')}`);

    console.log(JSON.stringify({ status: 'PASS', checks: 17, pageErrors: pageErrors.length, evidenceDir }));
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
