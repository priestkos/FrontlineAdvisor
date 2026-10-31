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

/** Fixed timeline (ms from start). Same on every run. */
const TIMELINE = [
  { at: 0, scene: "disclaimer", caption: "UI mock-up — not from the live game", fn: "resetInteractions" },
  { at: 3500, scene: "active", caption: "Main window (in match) — standings, FOCUS, THREAT, posture" },
  { at: 6500, fn: "pressTestButton", caption: "/fla test — sample lead-change notice" },
  { at: 9000, fn: "unpressTestButton", scene: "active", caption: "Map playbook note · poll footer · Settings" },
  { at: 11500, scene: "idle", caption: "Main window (idle) — waiting for a Frontline match" },
  { at: 14500, scene: "settings", caption: "Settings (/fla config) — Enabled, Announce, How" },
  { at: 17000, fn: "toggleWinClose", caption: "Announce: toggle Win close" },
  { at: 19000, fn: "bumpRateSlider", caption: "Tuning: rate window slider" },
  { at: 21000, fn: "openTeamCombo", caption: "Your team combo (Grand Company)" },
  { at: 23500, scene: "notify", caption: "Notifications — chat lines, server info bar (FR: …)" },
  { at: 25500, fn: "showToast", caption: "Screen toast on lead change" },
  { at: 28500, scene: "notify", caption: "/fla off — notices off; window still reads scores" },
  { at: 31000, scene: "commands", caption: "Slash commands — /fla on · off · team · dump · test · config" },
  { at: 33000, fn: "highlightCommand", arg: "dump", caption: "/fla dump — live scores to chat and /xllog" },
  { at: 35000, fn: "highlightCommand", arg: "test", caption: "/fla test" },
  { at: 37000, fn: "highlightCommand", arg: "config", caption: "/fla config opens Settings" },
  { at: 39000, scene: "disclaimer", caption: "UI mock-up — not from the live game", fn: "resetInteractions" },
];

const TOTAL_MS = 42000;

function runFfmpeg(args) {
  execFileSync("ffmpeg", ["-y", ...args], { stdio: "inherit" });
}

async function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function applyStep(page, step) {
  if (step.scene) {
    await page.evaluate((s) => window.Walkthrough.showScene(s), step.scene);
  }
  if (step.caption) {
    await page.evaluate((c) => window.Walkthrough.setCaption(c), step.caption);
  }
  if (step.fn) {
    await page.evaluate(
      ({ fn, arg }) => {
        const w = window.Walkthrough;
        if (arg !== undefined) w[fn](arg);
        else w[fn]();
      },
      { fn: step.fn, arg: step.arg }
    );
  }
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

  let cursor = 0;
  for (const step of TIMELINE) {
    const wait = step.at - cursor;
    if (wait > 0) await sleep(wait);
    cursor = step.at;
    await applyStep(page, step);
  }
  const tail = TOTAL_MS - cursor;
  if (tail > 0) await sleep(tail);

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
    "fps=6,scale=800:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5",
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
