using System;
using System.Collections.Generic;

namespace FrontlineAdvisor.Core;

/// <summary>The three Grand Companies that make up the Frontline teams, in score order.</summary>
public enum GrandCompanyTeam
{
    Maelstrom = 0,
    TwinAdder = 1,
    ImmortalFlames = 2,
}

/// <summary>Static helpers over the team enum. Pure: no Dalamud types, so the offline harness links it.</summary>
public static class Teams
{
    public static readonly IReadOnlyList<GrandCompanyTeam> All =
    [
        GrandCompanyTeam.Maelstrom,
        GrandCompanyTeam.TwinAdder,
        GrandCompanyTeam.ImmortalFlames,
    ];

    public static string Name(this GrandCompanyTeam team) => team switch
    {
        GrandCompanyTeam.Maelstrom => "Maelstrom",
        GrandCompanyTeam.TwinAdder => "Twin Adder",
        GrandCompanyTeam.ImmortalFlames => "Immortal Flames",
        _ => "Unknown",
    };

    public static string Short(this GrandCompanyTeam team) => team switch
    {
        GrandCompanyTeam.Maelstrom => "MAELSTROM",
        GrandCompanyTeam.TwinAdder => "ADDERS",
        GrandCompanyTeam.ImmortalFlames => "FLAMES",
        _ => "?",
    };

    /// <summary>The Grand Company's colour as 0..1 RGB, matching how the company appears in game.</summary>
    public static (float R, float G, float B) Colour(this GrandCompanyTeam team) => team switch
    {
        GrandCompanyTeam.Maelstrom => (0.87f, 0.31f, 0.27f),
        GrandCompanyTeam.TwinAdder => (0.94f, 0.80f, 0.28f),
        GrandCompanyTeam.ImmortalFlames => (0.34f, 0.61f, 0.95f),
        _ => (0.70f, 0.70f, 0.70f),
    };

    /// <summary>Parses a friendly team name: mael/adder/flame, full names, or the map colours.</summary>
    public static bool TryParse(string? text, out GrandCompanyTeam team)
    {
        team = GrandCompanyTeam.Maelstrom;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var t = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
        switch (t)
        {
            case "maelstrom":
            case "mael":
            case "storm":
            case "red":
                team = GrandCompanyTeam.Maelstrom;
                return true;
            case "twinadder":
            case "adder":
            case "adders":
            case "serpent":
            case "yellow":
                team = GrandCompanyTeam.TwinAdder;
                return true;
            case "immortalflames":
            case "immortalflame":
            case "flame":
            case "flames":
            case "blue":
                team = GrandCompanyTeam.ImmortalFlames;
                return true;
            default:
                return false;
        }
    }
}
