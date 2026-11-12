const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');

const baseUrl = process.env.QA_BASE_URL || 'http://127.0.0.1:5201';
const username = process.env.QA_USERNAME;
const password = process.env.QA_PASSWORD;
const evidenceDir = path.resolve(
  process.env.QA_EVIDENCE_DIR || path.join(__dirname, '../../Docs/QA/rfc-001-responsive')
);

if (!['127.0.0.1', 'localhost'].includes(new URL(baseUrl).hostname)) {
  throw new Error('La prueba solo permite un servidor local.');
}
if (!username || !password) {
  throw new Error('Define QA_USERNAME y QA_PASSWORD para iniciar sesión en Petmor.');
}

const viewports = [
  { name: 'desktop', width: 1365, height: 900 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'mobile', width: 390, height: 844 },
];
const routes = [
  '/',
  '/Productos',
  '/Productos/Create',
  '/Clientes',
  '/Clientes/Create',
  '/Ventas',
  '/Ventas/Create',
  '/Caja',
  '/Configuracion',
  '/Petmor/Agenda',
];

async function main() {
  fs.mkdirSync(evidenceDir, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const page = await browser.newPage({ viewport: viewports[0] });
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));

  try {
    await page.goto(`${baseUrl}/Identity/Account/Login`, { waitUntil: 'domcontentloaded' });
    await page.locator('#Input_Email').fill(username);
    await page.locator('#Input_Password').fill(password);
    await page.locator('button[type="submit"]').click();
    await page.waitForURL(url => !url.pathname.includes('/Identity/Account/Login'));

    let checkedPages = 0;
    for (const viewport of viewports) {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });

      for (const route of routes) {
        const response = await page.goto(`${baseUrl}${route}`, { waitUntil: 'domcontentloaded' });
        if (!response || response.status() !== 200) {
          throw new Error(`${route} respondió ${response?.status() ?? 'sin respuesta'} en ${viewport.name}.`);
        }

        const metrics = await page.evaluate(() => {
          const main = document.querySelector('.main-content');
          const mainRect = main?.getBoundingClientRect();
          const menuButton = document.querySelector('[aria-label="Abrir menú principal"]');
          return {
            viewportWidth: window.innerWidth,
            documentWidth: document.documentElement.scrollWidth,
            bodyWidth: document.body.scrollWidth,
            mainRight: mainRect?.right ?? null,
            menuButtonVisible: menuButton ? getComputedStyle(menuButton).display !== 'none' : false,
          };
        });

        if (metrics.documentWidth > viewport.width || metrics.bodyWidth > viewport.width) {
          throw new Error(`${route} presenta desbordamiento horizontal en ${viewport.name}: ${JSON.stringify(metrics)}.`);
        }
        if (metrics.mainRight !== null && metrics.mainRight > viewport.width + 1) {
          throw new Error(`${route} desborda el área principal en ${viewport.name}: ${JSON.stringify(metrics)}.`);
        }
        if (metrics.menuButtonVisible !== (viewport.width < 992)) {
          throw new Error(`El botón del menú no corresponde al breakpoint en ${viewport.name}.`);
        }

        checkedPages += 1;
        console.log(JSON.stringify({ status: 'PASS', viewport: viewport.name, route, ...metrics }));

        if (route === '/') {
          await page.screenshot({
            path: path.join(evidenceDir, `dashboard-${viewport.name}.png`),
            fullPage: true,
            style: '#userMenu .fw-bold { visibility: hidden !important; }',
          });
        }
      }

      if (viewport.width < 992) {
        await page.goto(`${baseUrl}/`, { waitUntil: 'domcontentloaded' });
        const menuButton = page.getByRole('button', { name: 'Abrir menú principal' });
        await menuButton.click();
        await page.locator('#sidebar-wrapper.show').waitFor({ state: 'visible' });
        await page.locator('#sidebar-wrapper [aria-label="Cerrar menú"]').click();
        await page.waitForFunction(() => !document.querySelector('#sidebar-wrapper').classList.contains('show'));
        console.log(JSON.stringify({ status: 'PASS', viewport: viewport.name, check: 'mobile menu opens and closes' }));
      }
    }

    if (pageErrors.length) {
      throw new Error(`Errores JavaScript: ${pageErrors.join(' | ')}`);
    }
    console.log(JSON.stringify({ status: 'PASS', checkedPages, pageErrors: pageErrors.length, evidenceDir }));
  } finally {
    await browser.close();
  }
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
