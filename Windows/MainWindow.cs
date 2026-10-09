using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FrontlineAdvisor.Core;

namespace FrontlineAdvisor.Windows;

/// <summary>
/// The advisor window. Draws only state the plugin already computed - no game calls here,
/// so the worst a drawing bug can do is look wrong.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Accent = new(0.98f, 0.80f, 0.28f, 1f);
    private static readonly Vector4 TextMain = new(0.95f, 0.96f, 1f, 1f);
    private static readonly Vector4 TextDim = new(0.62f, 0.66f, 0.78f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.44f, 0.44f, 1f);
    private static readonly Vector4 ThreatColour = new(0.95f, 0.66f, 0.30f, 1f);

    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Frontline Advisor###FrontlineAdvisorMain")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize;
        Size = new Vector2(436, 0) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * ImGuiHelpers.GlobalScale);

        DrawHeader();

        if (!plugin.InFrontline || plugin.ActiveMap is null || plugin.Tracker.Latest is null)
        {
            DrawIdle();
            return;
        }

        var map = plugin.ActiveMap;
        var analysis = plugin.Tracker.Latest;
        var briefing = Advisor.Build(analysis, plugin.LocalTeam);

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted($"{map.Display}  \u00b7  win at {map.Target:N0}");

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
            ImGui.TextWrapped(briefing.Headline);

        ImGui.Spacing();
        DrawStandings(analysis);

        ImGui.Spacing();
        DrawPanel(briefing.FocusTitle, briefing.FocusBody, Accent);
        DrawPanel(briefing.ThreatTitle, briefing.ThreatBody, ThreatColour);

        if (briefing.PostureBody is not null)
            DrawPanel("YOUR POSTURE", briefing.PostureBody, TextMain);

        if (briefing.MapNote.Length > 0)
            DrawPanel(briefing.MapHeading.ToUpperInvariant(), briefing.MapNote, TextDim);

        ImGui.Spacing();
        DrawFooter();
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.18f);
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Frontline Advisor");
        ImGui.SetWindowFontScale(1f);

        var label = plugin.Config.Enabled ? "Notices: on" : "Notices: off";
        var width = ImGui.CalcTextSize(label).X + 16 * ImGuiHelpers.GlobalScale;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - width);
        using (ImRaii.PushColor(ImGuiCol.Text, plugin.Config.Enabled ? TextDim : Danger))
            ImGui.TextUnformatted(label);
    }

    private void DrawIdle()
    {
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
            ImGui.TextWrapped("Not in a Frontline match.");

        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Queue for Frontline and the board fills in on its own. The scoreboard, the focus call and the lead-change notifications all start with the first reading.");
            ImGui.Spacing();
            ImGui.TextWrapped("Set your own Grand Company with /fla team mael | adder | flame for sharper 1st/2nd/3rd advice. If the board ever looks wrong, /fla dump prints what the plugin is reading.");
        }

        ImGui.Spacing();
        DrawFooter();
    }

    private void DrawStandings(MatchAnalysis analysis)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;
        if (!ImGui.BeginTable("##fa_standings", 5, flags)) return;

        ImGui.TableSetupColumn("Team", ImGuiTableColumnFlags.WidthFixed, 126 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Pts", ImGuiTableColumnFlags.WidthFixed, 54 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Gap", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("/min", ImGuiTableColumnFlags.WidthFixed, 52 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("To win", ImGuiTableColumnFlags.WidthStretch);

        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Team");
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted("Pts");
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted("Gap");
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted("/min");
            ImGui.TableSetColumnIndex(4); ImGui.TextUnformatted("To win");
        }

        var myTeam = plugin.LocalTeam;

        for (var i = 0; i < analysis.Standings.Count; i++)
        {
            var row = analysis.Standings[i];
            var colour = row.Team.Colour();
            var teamColour = new Vector4(colour.R, colour.G, colour.B, 1f);
            var isLeader = analysis.Leader == row.Team;

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            using (ImRaii.PushColor(ImGuiCol.Text, teamColour))
            {
                var mark = isLeader ? "\u25b2 " : "  ";
                var you = myTeam == row.Team ? " \u00b7 you" : string.Empty;
                ImGui.TextUnformatted($"{mark}{row.Team.Name()}{you}");
            }

            ImGui.TableSetColumnIndex(1);
            using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
                ImGui.TextUnformatted(row.Score.ToString("N0"));

            ImGui.TableSetColumnIndex(2);
            using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
                ImGui.TextUnformatted(analysis.Gap(row.Team).ToString("N0"));

            ImGui.TableSetColumnIndex(3);
            var rising = row.PerMinute >= 1.0;
            using (ImRaii.PushColor(ImGuiCol.Text, rising ? ThreatColour : TextDim))
                ImGui.TextUnformatted(row.PerMinute >= 0.5 || row.PerMinute <= -0.5 ? $"{row.PerMinute:+0;-0;0}" : "\u2013");

            ImGui.TableSetColumnIndex(4);
            using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            {
                if (analysis.ToWin.TryGetValue(row.Team, out var toWin) && toWin >= 0)
                {
                    var text = toWin.ToString("N0");
                    if (analysis.EtaWinMinutes.TryGetValue(row.Team, out var eta) && eta is not null)
                        text += $"  (~{Advisor.FormatMinutes(eta.Value)})";
                    ImGui.TextUnformatted(text);
                }
                else
                {
                    ImGui.TextUnformatted("\u2013");
                }
            }
        }

        ImGui.EndTable();
    }

    private static void DrawPanel(string title, string body, Vector4 titleColour)
    {
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, titleColour))
            ImGui.TextUnformatted(title);

        using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
            ImGui.TextWrapped(body);
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
        {
            if (plugin.InFrontline && plugin.LastReadUtc != default)
            {
                var age = (DateTime.UtcNow - plugin.LastReadUtc).TotalSeconds;
                ImGui.TextUnformatted($"Reading every second \u00b7 last {age:0.0}s ago \u00b7 Battle High {plugin.Snapshot.BattleHigh}");
            }
            else
            {
                ImGui.TextUnformatted("Waiting for a match.");
            }
        }

        ImGui.Spacing();

        var half = (ImGui.GetContentRegionAvail().X - 6 * ImGuiHelpers.GlobalScale) / 2f;

        using (ImRaii.PushColor(ImGuiCol.Button, Accent))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.88f, 0.45f, 1f)))
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.06f, 0.06f, 0.09f, 1f)))
        {
            if (ImGui.Button("Settings", new Vector2(half, 0))) plugin.OpenConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("Test notice", new Vector2(-1, 0))) plugin.FireTestNotice();
    }

    public void Dispose() { }
}
