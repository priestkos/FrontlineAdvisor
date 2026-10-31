const puppeteer = require("puppeteer-core");
const path = require("path");

const ROOT = path.join(__dirname);
const OUT = path.join(ROOT, "..");

const jobs = [
  ["01-main-window-active.html", "01-main-window-active.png", 560, 920],
  ["02-main-window-idle.html", "02-main-window-idle.png", 560, 520],
  ["03-settings-window.html", "03-settings-window.png", 520, 780],
  ["04-notifications-overlay.html", "04-notifications-overlay.png", 1280, 720],
  ["05-commands-help.html", "05-commands-help.png", 720, 520],
];

(async () => {
  const browser = await puppeteer.launch({
    executablePath: "/usr/local/bin/google-chrome",
    headless: true,
    args: ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
  });
  for (const [html, png, w, h] of jobs) {
    const page = await browser.newPage();
    await page.setViewport({ width: w, height: h, deviceScaleFactor: 1 });
    await page.goto(`file://${path.join(ROOT, html)}`, { waitUntil: "networkidle0", timeout: 15000 });
    await page.screenshot({ path: path.join(OUT, png), fullPage: true });
    await page.close();
    console.log("Wrote", png);
  }
  await browser.close();
})();
