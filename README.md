# Frontline Advisor

Reads the live Frontline scoreboard and says who is winning, who to focus, who is about to matter — and when the lead changes.

`/fla` · Dalamud API 15 · .NET 10

## What it does

- **Standings** — leader, gap to second, points left.
- **Focus** — the team closest to winning, and the doctrine for hitting them.
- **Threat** — the fastest scorer, and when they overtake.
- **Lead change** — chat line and toast when another company takes the lead.

Doctrine: play the leader, not the scoreboard; deny their rate; never fight the team just below you; your enemy is the clock, the rate, the stalemate.

## Screenshots

Screenshots are mock-ups illustrating the UI, not captures from the live game.

| | |
| --- | --- |
| ![Main window during a match](docs/screenshots/01-main-window-active.png) | **Main window (in match)** — standings table, headline, FOCUS / THREAT / YOUR POSTURE, map playbook note, poll footer, Settings and Test notice. |
| ![Main window idle](docs/screenshots/02-main-window-idle.png) | **Main window (idle)** — “Not in a Frontline match” help text and the same footer actions. |
| ![Settings window](docs/screenshots/03-settings-window.png) | **Settings** (`/fla config`) — Enabled, Announce, How, Your team, Map, and Tuning sliders. |
| ![Notifications overlay](docs/screenshots/04-notifications-overlay.png) | **Notifications** — chat lines (FRONTLINE tags), screen toast, server info bar (`FR: …`), plus `/fla dump` output and notices off. |
| ![Slash commands](docs/screenshots/05-commands-help.png) | **Slash commands** — `/fla`, `on`/`off`, `team`, `map`, `dump`, `test`, and `config` chat responses. |

### Mock-up video

The clip below is the same HTML mock-ups in sequence — **not** footage from the live game.

![Mock-up walkthrough preview](docs/videos/mockup-walkthrough.gif)

[Download mockup-walkthrough.mp4](docs/videos/mockup-walkthrough.mp4) (H.264, 1280×720)

### Feature coverage

| Feature | Screenshot |
| --- | --- |
| Main advisor window (`/fla`, Dalamud “Open Main UI”) | 01, 02 |
| Standings (Team, Pts, Gap, /min, To win) + leader marker | 01 |
| Headline, map line (`win at …`) | 01 |
| FOCUS panel | 01 |
| THREAT SOON panel | 01 |
| YOUR POSTURE (when your team is set) | 01 |
| Map playbook note (per-campaign heading + body) | 01 |
| Notices on/off indicator in window header | 01, 04 |
| Footer: poll age, Battle High; Settings; Test notice | 01, 02 |
| Idle / waiting-for-match state | 02 |
| Settings window (`/fla config` / `cfg` / `settings`, Settings button) | 03 |
| Enabled master switch | 03 |
| Announce: Lead change, First lead, Emerging threat, Win close | 03 |
| How: Chat line, Screen toast, Server info bar | 03, 04 |
| Your team combo (`auto`, Grand Companies) | 03 |
| Map combo (`auto`, Secure, Seize, Shatter, Danshig Naadam, Triumph) | 03 |
| Tuning: rate window, start grace, surge rate, win margin, lead/threat cooldowns | 03 |
| Chat notices (LEAD CHANGE, FIRST LEAD, THREAT, WIN CLOSE) | 04 |
| Screen toast | 04 |
| Server info bar entry (`FR: …`, click opens main window) | 04 |
| `/fla on` / `/fla off` (master notices) | 04, 05 |
| `/fla team` (`mael` \| `adder` \| `flame` \| `auto`, alias `me`) | 04, 05 |
| `/fla map` (`<name>` \| `auto`) | 05 |
| `/fla dump` (chat + `/xllog`) | 04, 05 |
| `/fla test` (sample lead-change notice) | 01, 05 |

## How it works

Three scores live in the instance-content director, beside Battle High; read through `EventFramework.GetInstanceContentDirector()` and a **read-only** overlay struct:

| Offset from the director | Field |
|---|---|
| `+0x24D2` | Maelstrom score (short) |
| `+0x24F2` | Twin Adder score (short) |
| `+0x2512` | Immortal Flames score (short) |
| `+0x33B7` | your Battle High (byte) |

- **No hooks, no signatures, no writes.**
- Content id (`GameMain.CurrentContentFinderConditionId`) decides the map first, so Crystalline Conflict and Rival Wings — a different director layout — are never read.
- Scores are range-checked: a stale offset shows *no reading*, not a wrong number, plus a `/xllog` line; `/fla dump` prints live values so it can be re-derived in one match.
- Rate is a **least-squares slope over a trailing window** (90 s default), so one odd tick cannot fake a surge.

## Commands

| Command | Effect |
| --- | --- |
| `/fla` | Toggle the window. |
| `/fla on` / `/fla off` | Master notification switch. |
| `/fla team mael\|adder\|flame\|auto` | Your Grand Company: 1st/2nd/3rd posture wording. |
| `/fla map <name\|auto>` | Force a map if detection is wrong. |
| `/fla dump` | Print live content id, director pointer and raw scores to chat and `/xllog`. |
| `/fla test` | Fire a sample lead-change notice. |
| `/fla config` | Open settings. |

## Settings

- **Announce** — lead change, first lead, emerging threat, win close.
- **How** — chat line, screen toast, server info bar entry (the leader's score, shown only during a match).
- **Your team** — auto (absolute advice) or a Grand Company (sharper posture); **Map** — auto or forced.
- **Tuning** — rate window, start grace, surge threshold, win margin, the two cooldowns.

## Build

Requires the .NET 10 SDK and the Dalamud dev distribution (`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or `DALAMUD_HOME`).

```
dotnet build -c Debug      # load bin/Debug/FrontlineAdvisor.dll as a dev plugin
dotnet build -c Release    # zip in bin/Release/FrontlineAdvisor/latest.zip
```

## Install (dev plugin)

1. `/xlsettings` → Experimental → Dev Plugin Locations → add `bin\Debug\FrontlineAdvisor.dll`.
2. `/xlplugins` → Dev Tools → enable **Frontline Advisor**.
3. `/fla` opens the window; `/fla config` opens settings.

## Tests

`tests/LogicTests` compiles the pure `Core/*.cs` with no Dalamud, dev distribution or game, and plays whole matches through the analyzer, advisor and tracker. CI runs it on every push and PR.

```
cd tests/LogicTests && dotnet run -c Release
# 75 passed, 0 failed
```

## Limits

- Offsets and content ids are pinned to the patch 7.5-era build; a PvP patch can move them. `/fla dump` spots and fixes it; the plugin degrades to "no reading" rather than guessing.
- Not verified in game: behaviour is taken from the code and the offline tests, not from a live client.

## Layout

```
Core/                 analyzer, advisor, tracker, teams, maps
FrontlineDirector.cs  read-only overlay, defensive reader
Notifier.cs           chat line + toast
Configuration.cs      IPluginConfiguration
Windows/              window, settings
Plugin.cs             services, update loop, commands
tests/LogicTests/     offline harness
```
