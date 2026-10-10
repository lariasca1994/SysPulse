// Genera los PNG de la PWA desde public/icons/icon.svg. Uso: npm run iconos
import { chromium } from "@playwright/test";
import { readFileSync } from "node:fs";
const svg = readFileSync("public/icons/icon.svg", "utf8");
// Maskable: fondo a sangre completa y el dibujo dentro de la zona segura (80 %).
const maskable = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect width="512" height="512" fill="#0B2A5B"/><g transform="translate(76.8 76.8) scale(0.7)"><polyline points="72,280 168,280 208,176 264,352 312,232 344,280 400,280" fill="none" stroke="#00C8FF" stroke-width="36" stroke-linecap="round" stroke-linejoin="round"/><circle cx="420" cy="140" r="36" fill="#E1251B"/></g></svg>`;
const browser = await chromium.launch();
const page = await browser.newPage();
for (const [archivo, contenido, tam] of [["icon-192.png", svg, 192], ["icon-512.png", svg, 512], ["icon-maskable-512.png", maskable, 512]]) {
  await page.setViewportSize({ width: tam, height: tam });
  await page.setContent(`<html><body style="margin:0;background:transparent">${contenido.replace("<svg ", `<svg width="${tam}" height="${tam}" `)}</body></html>`);
  await page.locator("svg").screenshot({ path: `public/icons/${archivo}`, omitBackground: true });
  console.log("ok", archivo);
}
await browser.close();
