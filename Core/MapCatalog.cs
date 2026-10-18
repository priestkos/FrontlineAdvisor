using System;
using System.Collections.Generic;
using System.Linq;

namespace FrontlineAdvisor.Core;

/// <summary>
/// A Frontline map: the content row that identifies it, its campaign name and its win target.
/// Targets are the patch 7.5-era numbers from the Frontline guide. Verify the HUD if a PvP patch moves them.
/// </summary>
public sealed record FrontlineMap(uint DutyId, string Zone, string Campaign, int Target)
{
    public string Display => $"{Zone} ({Campaign})";

    /// <summary>Per-map playbook note, condensed from the guide.</summary>
    public string Note => Campaign switch
    {
        "Secure" =>
            "Hold 4+ nodes - the 3->4 breakpoint is 6 -> 10 per tick. Nodes are worth +150. Collapse to the mid pillar on the weaponry timer and fight on the guardrailed ramps, never near the pad edges.",
        "Seize" =>
            "Tomeliths are stealable: win the fight at the node, then channel. A KO adds your Data and subtracts theirs, so an S-rank fight counts twice.",
        "Shatter" =>
            "Battle High converts straight into score here. Small ice always activates at 12:30 and 4:30 remaining. Never attack another company's large ice from the middle field - that lane invites the third team's pinch.",
        "Danshig Naadam" =>
            "Claims, not kills: a claimed ovoo cannot be stolen. B/A/S pay 50/100/200 over 30s. Put one player on the ovoo and screen outward - a single ranged hit breaks an enemy claim.",
        "Triumph" =>
            "One player per field; extra bodies do not help; any enemy inside blocks capture. Snow ~16:00 blocks the gully crossroads; auroras ~9:00 raise ranks and fill everyone's Limit Break - hold it.",
        _ => string.Empty,
    };

    /// <summary>How much of the map's data must be left before the win becomes a five-minute problem.</summary>
    public string NoteHeading => Campaign switch
    {
        "Secure" => "Secure: the node ring",
        "Seize" => "Seize: hold and contest",
        "Shatter" => "Shatter: the damage race",
        "Danshig Naadam" => "Danshig Naadam: first to the ovoo",
        "Triumph" => "Triumph: spread thin, then concentrate",
        _ => "Map",
    };
}

public static class MapCatalog
{
    /// <summary>
    /// The Frontline content rows. These are the same ids a maintained Frontline stat plugin keys on
    /// against GameMain::CurrentContentFinderConditionId, so a duty id here means "this is a Frontline map".
    /// If a future patch renames or renumbers a campaign, /fla dump prints the live id.
    /// </summary>
    public static readonly IReadOnlyList<FrontlineMap> Maps =
    [
        new FrontlineMap(127, "The Borderland Ruins", "Secure", 2400),
        new FrontlineMap(130, "Seal Rock", "Seize", 2000),
        new FrontlineMap(180, "The Fields of Glory", "Shatter", 1600),
        new FrontlineMap(701, "Onsal Hakair", "Danshig Naadam", 1400),
        new FrontlineMap(1080, "Worqor Chirteh", "Triumph", 1400),
    ];

    public static FrontlineMap? ByDutyId(uint id) => Maps.FirstOrDefault(m => m.DutyId == id);

    /// <summary>
    /// Lookup by duty id, exact campaign/zone/display, or a distinctive fragment.
    /// Fragments shorter than 3 characters are rejected so "/fla map a" cannot bind the first entry.
    /// </summary>
    public static FrontlineMap? ByName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var t = text.Trim();
        if (t.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;

        if (uint.TryParse(t, out var id)) return ByDutyId(id);

        // Prefer exact matches on campaign, zone, or the full display string.
        var exact = Maps.FirstOrDefault(m =>
            m.Campaign.Equals(t, StringComparison.OrdinalIgnoreCase) ||
            m.Zone.Equals(t, StringComparison.OrdinalIgnoreCase) ||
            m.Display.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Distinctive fragments only (e.g. "onsal", "chirteh", "naadam").
        if (t.Length < 3) return null;

        return Maps.FirstOrDefault(m =>
            m.Zone.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            m.Campaign.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            m.Display.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
