/**
 * Deterministic mock-up walkthrough: Playwright screen recording → H.264 MP4 + GIF.
 * Not live game footage — HTML mock-ups only.
 */
const { chromium } = require("playwright-core");
const { execFileSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = __dirname;
const HTML = path.join(ROOT, "walkthrough.html");
const OUT_DIR = path.join(ROOT, "..");
const TMP = path.join(ROOT, ".render-tmp");

const VIEWPORT = { width: 1280, height: 720 };
const CHROME = "/usr/local/bin/google-chrome";

function runFfmpeg(args) {
  execFileSync("ffmpeg", ["-y", ...args], { stdio: "inherit" });
}

async function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function callWalkthrough(page, method, ...args) {
  await page.evaluate(
    async ({ method, args }) => {
      const w = window.Walkthrough;
      const fn = w[method];
      if (typeof fn !== "function") throw new Error(`Walkthrough.${method} missing`);
      await fn(...args);
    },
    { method, args }
  );
}

async function setCaption(page, text, fade = false) {
  await page.evaluate(
    async ({ text, fade }) => {
      if (fade) await window.Walkthrough.setCaptionFade(text);
      else window.Walkthrough.setCaption(text);
    },
    { text, fade }
  );
}

/** Fixed sequence — same on every run. Durations include cursor travel and transitions. */
async function runTimeline(page) {
  await page.evaluate(() => {
    window.Walkthrough.showSceneInstant("disclaimer");
    window.Walkthrough.resetInteractions();
    window.Walkthrough.setCaption("UI mock-up — not from the live game");
  });
  await sleep(3400);

  await setCaption(page, "Main window (in match) — standings, FOCUS, THREAT, posture", true);
  await callWalkthrough(page, "showScene", "active");
  await sleep(2200);

  await setCaption(page, "/fla test — sample lead-change notice", true);
  await callWalkthrough(page, "clickTestNotice");
  await callWalkthrough(page, "releaseTestNotice");
  await sleep(800);

  await setCaption(page, "Map playbook note · poll footer · Settings", true);
  await sleep(1400);
  await callWalkthrough(page, "clickSettingsFromActive");
  await sleep(400);

  await setCaption(page, "Main window (idle) — waiting for a Frontline match", true);
  await callWalkthrough(page, "showScene", "idle");
  await sleep(2600);

  await setCaption(page, "Settings (/fla config) — Enabled, Announce, How", true);
  await callWalkthrough(page, "showScene", "settings");
  await sleep(2000);

  await setCaption(page, "Announce: toggle Win close", true);
  await callWalkthrough(page, "clickWinCloseCheckbox");
  await sleep(500);

  await setCaption(page, "Tuning: rate window slider", true);
  await callWalkthrough(page, "clickRateSlider");
  await sleep(400);

  await setCaption(page, "Your team combo (Grand Company)", true);
  await callWalkthrough(page, "clickTeamCombo");
  await sleep(600);

  await setCaption(page, "Notifications — chat lines, server info bar (FR: …)", true);
  await callWalkthrough(page, "showScene", "notify");
  await sleep(2200);

  await setCaption(page, "Screen toast on lead change", true);
  await callWalkthrough(page, "revealToast");
  await sleep(1200);

  await setCaption(page, "/fla off — notices off; window still reads scores", true);
  await sleep(2400);

  await setCaption(page, "Slash commands — /fla on · off · team · dump · test · config", true);
  await callWalkthrough(page, "showScene", "commands");
  await sleep(1800);

  await setCaption(page, "/fla dump — live scores to chat and /xllog", true);
  await callWalkthrough(page, "clickCommandRow", "dump");
  await sleep(300);

  await setCaption(page, "/fla test", true);
  await callWalkthrough(page, "clickCommandRow", "test");
  await sleep(300);

  await setCaption(page, "/fla config opens Settings", true);
  await callWalkthrough(page, "clickCommandRow", "config");
  await sleep(400);

  await setCaption(page, "UI mock-up — not from the live game", true);
  await callWalkthrough(page, "showScene", "disclaimer");
  await page.evaluate(() => window.Walkthrough.resetInteractions());
  await sleep(2800);
}

(async () => {
  fs.rmSync(TMP, { recursive: true, force: true });
  fs.mkdirSync(TMP, { recursive: true });

  const browser = await chromium.launch({
    executablePath: CHROME,
    headless: true,
    args: ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
  });

  const context = await browser.newContext({
    viewport: VIEWPORT,
    recordVideo: { dir: TMP, size: VIEWPORT },
  });
  const page = await context.newPage();
  await page.goto(`file://${HTML}`, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForFunction(() => typeof window.Walkthrough !== "undefined");

  await runTimeline(page);

  const video = page.video();
  await page.close();
  await context.close();
  await browser.close();

  const webmPath = await video.path();
  const mp4Path = path.join(OUT_DIR, "mockup-walkthrough.mp4");
  const gifPath = path.join(OUT_DIR, "mockup-walkthrough.gif");

  runFfmpeg([
    "-i",
    webmPath,
    "-c:v",
    "libx264",
    "-crf",
    "28",
    "-preset",
    "medium",
    "-pix_fmt",
    "yuv420p",
    "-movflags",
    "+faststart",
    "-an",
    mp4Path,
  ]);

  runFfmpeg([
    "-i",
    mp4Path,
    "-vf",
    "fps=5,scale=720:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=96:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=4",
    "-loop",
    "0",
    gifPath,
  ]);

  fs.rmSync(TMP, { recursive: true, force: true });

  const mp4Size = fs.statSync(mp4Path).size;
  const gifSize = fs.statSync(gifPath).size;
  console.log(`Wrote ${mp4Path} (${(mp4Size / 1024 / 1024).toFixed(2)} MiB)`);
  console.log(`Wrote ${gifPath} (${(gifSize / 1024 / 1024).toFixed(2)} MiB)`);
})().catch((err) => {
  console.error(err);
  process.exit(1);
});
