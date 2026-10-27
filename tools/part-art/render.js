// Renders every SVG in a folder to a PNG of the same name: node render.js <svg dir> <png dir> <width> <height>
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

(async () => {
  const [src, dst, w, h] = process.argv.slice(2);
  fs.mkdirSync(dst, { recursive: true });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: +w, height: +h } });
  for (const f of fs.readdirSync(src).filter(f => f.endsWith('.svg'))) {
    await page.setContent(`<html><body style="margin:0">${fs.readFileSync(path.join(src, f), 'utf8')}</body></html>`);
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({ path: path.join(dst, f.replace(/\.svg$/, '.png')), clip: { x: 0, y: 0, width: +w, height: +h } });
    console.log(f);
  }
  await browser.close();
})();
