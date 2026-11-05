import { chromium } from "playwright-core";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.resolve(__dirname, "..");

const CHROME = process.env.CHROME_PATH || "/usr/local/bin/google-chrome";

const scenes = [
  { html: "01-main-window-active.html", png: "01-main-window-active.png" },
  { html: "02-main-window-idle.html", png: "02-main-window-idle.png" },
  { html: "03-settings-window.html", png: "03-settings-window.png" },
  { html: "04-notifications-overlay.html", png: "04-notifications-overlay.png" },
  { html: "05-commands-help.html", png: "05-commands-help.png" },
];

const browser = await chromium.launch({
  executablePath: CHROME,
  headless: true,
  args: ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
});

const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 720 });

for (const { html, png } of scenes) {
  const fileUrl = `file://${path.join(__dirname, html)}`;
  await page.goto(fileUrl, { waitUntil: "load", timeout: 30000 });
  await page.waitForTimeout(120);
  await page.screenshot({
    path: path.join(outDir, png),
    type: "png",
  });
  console.log("Wrote", png);
}

await browser.close();
