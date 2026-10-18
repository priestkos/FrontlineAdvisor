# Simulated screenshots

HTML mock sources for the Frontline Advisor windows, plus cropped PNGs used in the repo README and `preview.html`.

| PNG | Source mock | Shows |
|---|---|---|
| `advisor_mid_match.png` | `mock-advisor.html` | Live board, focus, threat, posture, map note |
| `advisor_idle.png` | `mock-idle.html` | Waiting for a Frontline match |
| `advisor_settings.png` | `mock-settings.html` | Settings window |
| `advisor_lead_change.png` | `mock-notice.html` | Chat line + screen toast on lead change |

Regenerate (needs Chrome):

```bash
for name in advisor idle settings notice; do
  google-chrome --headless=new --no-sandbox --disable-gpu --hide-scrollbars \
    --window-size=700,1200 \
    --screenshot="tmp-${name}.png" \
    "file://$PWD/mock-${name}.html"
done
# then crop each tmp-*.png to the non-background bounding box
```

These are **not** captures from a live FFXIV / Dalamud client.
