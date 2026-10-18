using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrontlineAdvisor.Core;
using FrontlineAdvisor.Windows;

namespace FrontlineAdvisor;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Frontline Advisor";

    internal const string CommandName = "/fla";
    private const string DtrTitle = "Frontline Advisor";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly IDtrBar dtrBar;
    private readonly Notifier notifier;

    private readonly Configuration configuration;
    private readonly MatchTracker tracker = new();
    private readonly WindowSystem windowSystem = new("FrontlineAdvisor");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    private IDtrBarEntry? dtrEntry;
    private DateTime lastPollUtc = DateTime.MinValue;
    private DateTime? lastWarnUtc;
    private DirectorSnapshot snapshot;
    private FrontlineMap? activeMap;
    private DateTime lastReadUtc;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IClientState clientState,
        IFramework framework,
        IChatGui chatGui,
        IToastGui toastGui,
        IDtrBar dtrBar,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;
        this.clientState = clientState;
        this.framework = framework;
        this.chatGui = chatGui;
        this.dtrBar = dtrBar;
        this.log = log;

        configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();

        notifier = new Notifier(chatGui, toastGui, log);
        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Frontline Advisor. /fla opens the window; on|off, team <mael|adder|flame|auto>, map <name|auto>, dump, test, config.",
        });

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        framework.Update += OnFrameworkUpdate;

        UpdateDtrVisibility();

        log.Information("[FrontlineAdvisor] loaded.");
    }

    // ---- state the windows read ----

    internal Configuration Config => configuration;

    internal MatchTracker Tracker => tracker;

    internal DirectorSnapshot Snapshot => snapshot;

    internal FrontlineMap? ActiveMap => activeMap;

    internal bool InFrontline => activeMap is not null && snapshot.Ok;

    internal DateTime LastReadUtc => lastReadUtc;

    internal GrandCompanyTeam? LocalTeam => configuration.ResolveLocalTeam();

    internal void SaveConfiguration()
    {
        pluginInterface.SavePluginConfig(configuration);
        // ConfigWindow can flip ShowDtrBar; keep the server info bar entry in sync immediately.
        UpdateDtrVisibility();
        if (configuration.ShowDtrBar && InFrontline)
            UpdateDtr();
    }

    internal void OpenConfig() => configWindow.IsOpen = true;

    internal void FireTestNotice() => FireTest();

    // ---- update loop ----

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - lastPollUtc).TotalMilliseconds < 950) return;
            lastPollUtc = now;

            if (!clientState.IsPvP)
            {
                EndMatch();
                return;
            }

            // Resolve the map from the safe content-id read first: never touch the raw score offsets outside
            // a recognised Frontline map (Crystalline Conflict and Rival Wings use a different director layout).
            var map = ResolveMap(FrontlineReader.CurrentDutyId());
            if (map is null)
            {
                EndMatch();
                WarnThrottled("In PvP but not on a recognised Frontline map. Run /fla dump to see the live duty id.");
                return;
            }

            snapshot = FrontlineReader.Read();

            if (!snapshot.Ok)
            {
                // Keep activeMap so we can resume when the director becomes readable again, but never
                // present stale standings or a stale server-info-bar line as if they were live.
                ClearStalePresentation();
                WarnThrottled(snapshot.Note ?? "could not read the Frontline scores.");
                return;
            }

            if (activeMap is null || activeMap.DutyId != map.DutyId)
                StartMatch(now, map);

            lastReadUtc = now;

            var sample = ScoreSample.Of(now, snapshot.Maelstrom, snapshot.TwinAdder, snapshot.ImmortalFlames);
            var notices = tracker.Observe(sample, map, configuration.ToNotificationSettings());

            if (configuration.Enabled)
            {
                foreach (var notice in notices)
                    notifier.Emit(notice, configuration);
            }

            UpdateDtr();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FrontlineAdvisor] error in the update loop");
        }
    }

    private FrontlineMap? ResolveMap(uint dutyId)
    {
        // An explicit override always wins.
        if (!string.Equals(configuration.MapOverride, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var forced = MapCatalog.ByName(configuration.MapOverride);
            if (forced is not null) return forced;
        }

        // Duty / content-finder condition ids only. TerritoryType lives in a different ID space and
        // must not be looked up against Frontline duty ids (false positives on other PvP content).
        return MapCatalog.ByDutyId(dutyId);
    }

    /// <summary>
    /// Drops the last good analysis and hides the DTR line so a failed read cannot look like a live board.
    /// Does not call <see cref="EndMatch"/> — the map stays so a later good read resumes the same match.
    /// </summary>
    private void ClearStalePresentation()
    {
        tracker.ClearLatest();
        if (dtrEntry is not null) dtrEntry.Shown = false;
    }

    private void StartMatch(DateTime now, FrontlineMap map)
    {
        activeMap = map;
        tracker.Reset(now);
        log.Information($"[FrontlineAdvisor] entering {map.Display}.");
    }

    private void EndMatch()
    {
        if (activeMap is null) return;
        log.Information("[FrontlineAdvisor] leaving the Frontline match.");
        activeMap = null;
        tracker.Reset(DateTime.UtcNow);
        if (dtrEntry is not null) dtrEntry.Shown = false;
    }

    private void WarnThrottled(string message)
    {
        var now = DateTime.UtcNow;
        if (lastWarnUtc is not null && (now - lastWarnUtc.Value).TotalSeconds < 60) return;
        lastWarnUtc = now;
        log.Warning("[FrontlineAdvisor] " + message);
    }

    // ---- server info bar ----

    private void UpdateDtrVisibility()
    {
        if (configuration.ShowDtrBar)
        {
            dtrEntry ??= dtrBar.Get(DtrTitle);
            dtrEntry.OnClick = _ => ToggleMain();
            dtrEntry.Shown = InFrontline;
        }
        else if (dtrEntry is not null)
        {
            dtrEntry.Shown = false;
        }
    }

    private void UpdateDtr()
    {
        if (!configuration.ShowDtrBar || dtrEntry is null) return;
        if (!InFrontline || tracker.Latest is null)
        {
            dtrEntry.Shown = false;
            return;
        }

        var a = tracker.Latest;
        var label = a.TiedLead ? "FR: tie" : $"FR: {a.Standings[0].Team.Short()} {a.Standings[0].Score:N0}";
        dtrEntry.Text = new SeStringBuilder().AddText(label).Build();
        dtrEntry.Tooltip = new SeStringBuilder().AddText(Advisor.Headline(a)).Build();
        dtrEntry.Shown = true;
    }

    // ---- commands ----

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            ToggleMain();
            return;
        }

        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (verb)
        {
            case "on":
                configuration.Enabled = true;
                SaveConfiguration();
                MainInfo("Advisor notices ON.");
                return;

            case "off":
                configuration.Enabled = false;
                SaveConfiguration();
                MainInfo("Advisor notices OFF.");
                return;

            case "team":
            case "me":
                SetTeam(rest);
                return;

            case "map":
                SetMap(rest);
                return;

            case "dump":
                Dump();
                return;

            case "test":
                FireTest();
                return;

            case "config":
            case "cfg":
            case "settings":
                ToggleConfig();
                return;

            default:
                MainInfo("Frontline Advisor: /fla (window) | on | off | team <mael|adder|flame|auto> | map <name|auto> | dump | test | config");
                return;
        }
    }

    private void SetTeam(string value)
    {
        if (value.Length == 0)
        {
            MainInfo($"Your team is '{configuration.LocalTeam}'. Use: /fla team mael | adder | flame | auto");
            return;
        }

        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            configuration.LocalTeam = "auto";
            SaveConfiguration();
            MainInfo("Your team set to auto (advice is phrased in absolute terms).");
            return;
        }

        if (!Teams.TryParse(value, out var team))
        {
            MainInfo($"Could not read '{value}'. Use: /fla team mael | adder | flame | auto");
            return;
        }

        configuration.LocalTeam = team.Name();
        SaveConfiguration();
        MainInfo($"Your team set to {team.Name()}.");
    }

    private void SetMap(string value)
    {
        if (value.Length == 0 || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            configuration.MapOverride = "auto";
            SaveConfiguration();
            MainInfo("Map detection set to auto.");
            return;
        }

        var map = MapCatalog.ByName(value);
        if (map is null)
        {
            MainInfo($"No Frontline map named '{value}'. Options: Secure, Seize, Shatter, Danshig Naadam, Triumph.");
            return;
        }

        configuration.MapOverride = map.Campaign;
        SaveConfiguration();
        MainInfo($"Map forced to {map.Display} (win at {map.Target:N0}).");
    }

    private void Dump()
    {
        var live = FrontlineReader.Read();
        var ptr = FrontlineReader.DirectorAddress();

        log.Information(
            $"[FrontlineAdvisor] dump territory={clientState.TerritoryType} dutyId={live.DutyId} " +
            $"contentTimeMax={live.ContentTimeMax} director=0x{ptr:X} rawOk={live.Ok} " +
            $"raw=[{live.Maelstrom},{live.TwinAdder},{live.ImmortalFlames}] bh={live.BattleHigh} " +
            $"map={(activeMap?.Display ?? "none")} note={live.Note ?? "-"}");

        MainInfo(
            $"dump: IsPvP={clientState.IsPvP} territory={clientState.TerritoryType} dutyId={live.DutyId} " +
            $"timeMax={live.ContentTimeMax} ok={live.Ok} scores=[{live.Maelstrom}/{live.TwinAdder}/{live.ImmortalFlames}] " +
            $"map={(activeMap?.Display ?? "none")}");
    }

    private void FireTest()
    {
        var sample = new Notice(
            NoticeKind.LeadChange,
            GrandCompanyTeam.Maelstrom,
            "Maelstrom takes the lead on 1,240 (60 clear of Twin Adder), up from Immortal Flames. New target: Maelstrom \u2014 deny their income, do not fight the team below you.",
            Urgent: true);

        notifier.Emit(sample, configuration);
        MainInfo("Test notice fired (a sample lead change).");
    }

    private void MainInfo(string message)
    {
        log.Information("[FrontlineAdvisor] " + message);
        try
        {
            chatGui.Print("[Frontline Advisor] " + message);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FrontlineAdvisor] could not print to chat");
        }
    }

    private void ToggleMain() => mainWindow.Toggle();

    private void ToggleConfig() => configWindow.Toggle();

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;

        pluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        pluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        commandManager.RemoveHandler(CommandName);

        if (dtrEntry is not null)
        {
            dtrEntry.Remove();
            dtrEntry = null;
        }

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
    }
}
