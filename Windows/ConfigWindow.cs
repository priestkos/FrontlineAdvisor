using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FrontlineAdvisor.Core;

namespace FrontlineAdvisor.Windows;

/// <summary>All settings. Nothing here touches the game; it edits the config and saves.</summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 TextDim = new(0.62f, 0.66f, 0.78f, 1f);
    private static readonly Vector4 Accent = new(0.98f, 0.80f, 0.28f, 1f);

    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("Frontline Advisor \u2014 Settings###FrontlineAdvisorConfig")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize;
        Size = new Vector2(440, 0) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * ImGuiHelpers.GlobalScale);

        var config = plugin.Config;
        var changed = false;

        changed |= Checkbox("Enabled", config.Enabled, v => config.Enabled = v, "Master switch. Off reads and shows, but says nothing.");

        ImGui.Spacing();
        Section("Announce");
        changed |= Checkbox("Lead change", config.AnnounceLeadChange, v => config.AnnounceLeadChange = v, "The explicit ask: someone else has the most points now.");
        changed |= Checkbox("First lead", config.AnnounceFirstLead, v => config.AnnounceFirstLead = v, "Name whoever grabs the lead first.");
        changed |= Checkbox("Emerging threat", config.AnnounceEmergingThreat, v => config.AnnounceEmergingThreat = v, "A team starts scoring fast and will matter soon.");
        changed |= Checkbox("Win close", config.AnnounceWinImminent, v => config.AnnounceWinImminent = v, "A team is within a margin of the target.");

        ImGui.Spacing();
        Section("How");
        changed |= Checkbox("Chat line", config.ShowInChat, v => config.ShowInChat = v, null);
        changed |= Checkbox("Screen toast", config.ShowToast, v => config.ShowToast = v, null);
        changed |= Checkbox("Server info bar", config.ShowDtrBar, v => config.ShowDtrBar = v, "Show the leading team's score in the server info bar during a match.");

        ImGui.Spacing();
        Section("Your team");
        changed |= TeamCombo(config);

        ImGui.Spacing();
        Section("Map");
        changed |= MapCombo(config);
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextWrapped("Auto follows the live content id. Force one only if detection is wrong.");

        ImGui.Spacing();
        Section("Tuning");
        changed |= SliderInt("Rate window (s)", config.RateWindowSeconds, 20, 300, v => config.RateWindowSeconds = v);
        changed |= SliderInt("Start grace (s)", config.GraceSeconds, 0, 90, v => config.GraceSeconds = v);
        changed |= SliderInt("Surge rate /min", config.SurgeRatePerMinute, 10, 300, v => config.SurgeRatePerMinute = v);
        changed |= SliderInt("Win margin", config.WinImminentMargin, 25, 600, v => config.WinImminentMargin = v);
        changed |= SliderInt("Lead cooldown (s)", config.LeadChangeCooldownSeconds, 0, 120, v => config.LeadChangeCooldownSeconds = v);
        changed |= SliderInt("Threat cooldown (s)", config.ThreatCooldownSeconds, 0, 180, v => config.ThreatCooldownSeconds = v);

        if (changed)
        {
            config.Normalize();
            plugin.SaveConfiguration();
        }

        ImGui.Spacing();
        ImGui.Separator();
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
        {
            ImGui.TextWrapped("/fla on | off | team | map | dump | test.  Frontline Advisor reads the game's own score board; it hooks nothing and writes nothing back.");
        }
    }

    private static bool Checkbox(string label, bool value, Action<bool> set, string? tooltip)
    {
        var local = value;
        if (ImGui.Checkbox(label, ref local)) set(local);

        if (tooltip is not null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return value != local;
    }

    private static bool SliderInt(string label, int value, int min, int max, Action<int> set)
    {
        var local = value;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt(label, ref local, min, max)) set(local);
        return value != local;
    }

    private static bool TeamCombo(Configuration config)
    {
        var options = new[] { "auto", "maelstrom", "twin adder", "immortal flames" };
        var preview = string.Equals(config.LocalTeam, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto (unknown)"
            : config.ResolveLocalTeam()?.Name() ?? "auto (unknown)";

        ImGui.SetNextItemWidth(-1);
        var changed = false;
        if (ImGui.BeginCombo("##fa_team", preview))
        {
            foreach (var option in options)
            {
                var selected = string.Equals(config.LocalTeam, option, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option, selected))
                {
                    config.LocalTeam = option;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextWrapped("Pick your Grand Company for 1st/2nd/3rd posture wording. Auto keeps the advice absolute.");

        return changed;
    }

    private static bool MapCombo(Configuration config)
    {
        var options = new[] { "auto", "Secure", "Seize", "Shatter", "Danshig Naadam", "Triumph" };
        var preview = string.Equals(config.MapOverride, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : MapCatalog.ByName(config.MapOverride)?.Display ?? config.MapOverride;

        ImGui.SetNextItemWidth(-1);
        var changed = false;
        if (ImGui.BeginCombo("##fa_map", preview))
        {
            foreach (var option in options)
            {
                var selected = string.Equals(config.MapOverride, option, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(option, selected))
                {
                    config.MapOverride = option;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    private static void Section(string title)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted(title);
    }

    public void Dispose() { }
}
