# Frontline Advisor

Reads the live Frontline score board and tells you three things, and tells you when to care:

- **Who has the most points right now** — the leader, the gap to second, and every team's points left to the win.
- **Who to focus** — the team closest to actually winning, with the doctrine for hitting them.
- **Who is about to be dangerous** — whoever is scoring fastest, with an estimated time to overtake the leader.
- **A shout when the leader changes** — a chat line and a toast the moment a different Grand Company takes the lead.

All of the advice comes from the Frontline guide in `D:\FFXIV Frontline` (and its canonical copy in the Obsidian
vault): *play the leader, not the scoreboard; deny their rate; never fight the team just below you; your enemy is the
clock / the rate / the stalemate.*

## How it reads the game

Frontline keeps its three team scores in the instance-content director, alongside the local player's Battle High.
The plugin reads them through `EventFramework.GetInstanceContentDirector()` and a small **read-only** overlay struct:

| Offset from the director | Field |
|---|---|
| `+0x24D2` | Maelstrom score (short) |
| `+0x24F2` | Twin Adder score (short) |
| `+0x2512` | Immortal Flames score (short) |
| `+0x33B7` | your Battle High (byte) |

- **No hooks, no signatures, no writes.** Nothing is patched into the game; the plugin only reads.
- The map is identified from the **content id** (`GameMain.CurrentContentFinderConditionId`) before any raw offset is
  touched, so Crystalline Conflict and Rival Wings — which use a different director layout — are never read.
- Every score is range-checked. If the offset ever goes stale after a PvP patch, the plugin shows *no reading*
  instead of a wrong number, and says so in `/xllog`. `/fla dump` prints the live values so the offset can be
  re-derived in one match.

Motion is measured as a **least-squares slope over a trailing window** (default 90s), so one odd tick cannot invent a
surge.

## Commands

| Command | What it does |
|---|---|
| `/fla` | Toggle the advisor window. |
| `/fla on` / `/fla off` | Master switch for the notifications. |
| `/fla team mael\|adder\|flame\|auto` | Your Grand Company, for 1st/2nd/3rd posture wording. |
| `/fla map <name\|auto>` | Force a map if detection is ever wrong. |
| `/fla dump` | Print the live content id, director pointer and raw scores to chat and `/xllog`. |
| `/fla test` | Fire a sample lead-change notice. |
| `/fla config` | Open settings. |

## Settings

- **Announce**: lead change (the explicit ask), first lead, emerging threat, win close.
- **How**: chat line, screen toast, server info bar entry.
- **Your team**: auto (absolute advice) or a specific Grand Company (sharper posture).
- **Tuning**: rate window, start grace, surge threshold, win margin, and the two cooldowns.

## Build

Needs the .NET 10 SDK and the Dalamud dev distrib at `%AppData%\XIVLauncher\addon\Hooks\dev`.

```
dotnet build -c Debug      # load bin/Debug/FrontlineAdvisor.dll as a dev plugin
dotnet build -c Release    # zip in bin/Release/FrontlineAdvisor/latest.zip
```

## Offline tests

`tests/LogicTests` compiles the pure logic files (`Core/*.cs`) from the plugin itself — no Dalamud, no dev distrib,
no game — and plays whole matches through the analyzer, advisor and notification tracker:

```
cd tests/LogicTests && dotnet run -c Release
# 66 checks, 0 failures
```

## Layout

| File | |
|---|---|
| `Core/` | teams, map catalog, analyzer, advisor text, notification tracker — no Dalamud types |
| `FrontlineDirector.cs` | the read-only score overlay and the defensive reader |
| `Notifier.cs` | chat line + toast emission |
| `Configuration.cs` | `IPluginConfiguration` |
| `Windows/` | the advisor window and settings |
| `Plugin.cs` | services, update loop, commands |
| `preview.html` | window mockup |

## Caveat

The score offsets and the content ids are pinned to the current game build (patch 7.5 era). A major PvP patch can move
them; `/fla dump` is the tool to spot and fix it, and the plugin degrades to "no reading" rather than guessing.
