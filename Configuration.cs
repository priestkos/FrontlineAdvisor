using System;
using Dalamud.Configuration;
using FrontlineAdvisor.Core;

namespace FrontlineAdvisor;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Master switch. Off means the plugin reads and shows, but says nothing.</summary>
    public bool Enabled { get; set; } = true;

    // ---- what to announce ----
    public bool AnnounceLeadChange { get; set; } = true;
    public bool AnnounceFirstLead { get; set; } = true;
    public bool AnnounceEmergingThreat { get; set; } = true;
    public bool AnnounceWinImminent { get; set; } = true;

    // ---- how to announce ----
    public bool ShowInChat { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public ushort AccentColourKey { get; set; } = 60;

    // ---- analysis tuning ----
    /// <summary>Trailing window the scoring rate is measured over, in seconds.</summary>
    public int RateWindowSeconds { get; set; } = 90;

    /// <summary>Seconds at the start of a match where nothing is announced (the scores are still settling).</summary>
    public int GraceSeconds { get; set; } = 20;

    /// <summary>Points per minute a non-leader must gain before it is called an emerging threat.</summary>
    public int SurgeRatePerMinute { get; set; } = 60;

    /// <summary>How close to the target counts as "about to win".</summary>
    public int WinImminentMargin { get; set; } = 150;

    public int LeadChangeCooldownSeconds { get; set; } = 20;
    public int ThreatCooldownSeconds { get; set; } = 30;

    // ---- identity ----
    /// <summary>Your Grand Company in the match, or "auto" (unknown). Auto phrases advice in absolute terms.</summary>
    public string LocalTeam { get; set; } = "auto";

    /// <summary>Force a map (a duty id, zone or campaign name) when auto-detection is wrong. "auto" is the default.</summary>
    public string MapOverride { get; set; } = "auto";

    // ---- display ----
    public bool ShowDtrBar { get; set; } = true;

    public NotificationSettings ToNotificationSettings() => new(
        AnnounceLeadChange,
        AnnounceFirstLead,
        AnnounceEmergingThreat,
        AnnounceWinImminent,
        GraceSeconds,
        RateWindowSeconds,
        SurgeRatePerMinute,
        WinImminentMargin,
        LeadChangeCooldownSeconds,
        ThreatCooldownSeconds);

    public GrandCompanyTeam? ResolveLocalTeam() => Teams.TryParse(LocalTeam, out var t) ? t : null;

    /// <summary>Repairs anything a hand-edited or half-written config could be missing.</summary>
    public void Normalize()
    {
        RateWindowSeconds = Math.Clamp(RateWindowSeconds, 10, 600);
        GraceSeconds = Math.Clamp(GraceSeconds, 0, 120);
        SurgeRatePerMinute = Math.Clamp(SurgeRatePerMinute, 1, 10000);
        WinImminentMargin = Math.Clamp(WinImminentMargin, 10, 2000);
        LeadChangeCooldownSeconds = Math.Clamp(LeadChangeCooldownSeconds, 0, 600);
        ThreatCooldownSeconds = Math.Clamp(ThreatCooldownSeconds, 0, 600);

        LocalTeam ??= "auto";
        if (!string.Equals(LocalTeam, "auto", StringComparison.OrdinalIgnoreCase) && !Teams.TryParse(LocalTeam, out _))
            LocalTeam = "auto";

        MapOverride ??= "auto";
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
