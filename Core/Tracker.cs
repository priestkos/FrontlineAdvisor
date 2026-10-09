using System;
using System.Collections.Generic;
using System.Linq;

namespace FrontlineAdvisor.Core;

public enum NoticeKind
{
    LeadChange,
    FirstLead,
    EmergingThreat,
    WinImminent,
}

/// <summary>One thing worth saying out loud.</summary>
public sealed record Notice(NoticeKind Kind, GrandCompanyTeam? Team, string Message, bool Urgent);

/// <summary>Pure notification settings, so the tracker can be tested without the config object.</summary>
public sealed record NotificationSettings(
    bool AnnounceLeadChange,
    bool AnnounceFirstLead,
    bool AnnounceEmergingThreat,
    bool AnnounceWinImminent,
    int GraceSeconds,
    int RateWindowSeconds,
    double SurgeRatePerMinute,
    int WinImminentMargin,
    int LeadChangeCooldownSeconds,
    int ThreatCooldownSeconds)
{
    public static NotificationSettings Default => new(
        AnnounceLeadChange: true,
        AnnounceFirstLead: true,
        AnnounceEmergingThreat: true,
        AnnounceWinImminent: true,
        GraceSeconds: 20,
        RateWindowSeconds: 90,
        SurgeRatePerMinute: 60,
        WinImminentMargin: 150,
        LeadChangeCooldownSeconds: 20,
        ThreatCooldownSeconds: 30);
}

/// <summary>
/// Holds a match's score history and decides what is worth announcing.
/// Pure: no Dalamud types, so the offline harness can play whole matches through it.
/// </summary>
public sealed class MatchTracker
{
    private readonly List<ScoreSample> history = [];
    private readonly Dictionary<(NoticeKind Kind, GrandCompanyTeam Team), DateTime> lastFired = [];
    private readonly HashSet<GrandCompanyTeam> winImminentFired = [];

    private MatchAnalysis? previous;
    private DateTime startUtc;
    private bool baselineEstablished;
    private bool firstLeadAnnounced;

    public MatchAnalysis? Latest { get; private set; }

    public DateTime StartUtc => startUtc;

    public bool Running => history.Count > 0;

    public int SampleCount => history.Count;

    /// <summary>Called when a match is joined (or re-joined).</summary>
    public void Reset(DateTime nowUtc)
    {
        history.Clear();
        lastFired.Clear();
        winImminentFired.Clear();
        previous = null;
        baselineEstablished = false;
        firstLeadAnnounced = false;
        Latest = null;
        startUtc = nowUtc;
    }

    /// <summary>Record one reading and return anything that should be announced.</summary>
    public List<Notice> Observe(ScoreSample sample, FrontlineMap? map, NotificationSettings settings)
    {
        if (history.Count == 0) startUtc = sample.TimeUtc;

        history.Add(sample);

        // Keep at most a generous window: enough to compute a steady rate, bounded memory.
        var keepFrom = sample.TimeUtc.AddSeconds(-Math.Max(settings.RateWindowSeconds * 5, 600));
        history.RemoveAll(s => s.TimeUtc < keepFrom);

        var analysis = Analyzer.Analyze(sample, history, map, settings.RateWindowSeconds);
        Latest = analysis;

        var notices = new List<Notice>();
        var elapsed = (sample.TimeUtc - startUtc).TotalSeconds;

        // Scores are still settling in the opening seconds; say nothing and build a baseline.
        if (elapsed < settings.GraceSeconds)
        {
            return notices;
        }

        if (!baselineEstablished)
        {
            baselineEstablished = true;
            previous = analysis;

            if (settings.AnnounceFirstLead && !firstLeadAnnounced && analysis.Leader is not null)
            {
                firstLeadAnnounced = true;
                var lead = analysis.Standings[0];
                notices.Add(new Notice(
                    NoticeKind.FirstLead,
                    analysis.Leader,
                    $"{analysis.Leader.Value.Name()} takes the first lead on {lead.Score:N0}. Focus {analysis.Leader.Value.Name()} \u2014 deny their objectives, and never chase them back to their platform.",
                    Urgent: false));
            }

            return notices;
        }

        BuildNotices(previous!, analysis, settings, sample.TimeUtc, notices);
        previous = analysis;
        return notices;
    }

    private void BuildNotices(
        MatchAnalysis prev,
        MatchAnalysis now,
        NotificationSettings settings,
        DateTime nowUtc,
        List<Notice> outp)
    {
        // --- Lead change: the explicit ask ---
        if (settings.AnnounceLeadChange && !now.TiedLead && now.Leader is not null)
        {
            var changed = prev.Leader != now.Leader;
            if (changed && TryCooldown(NoticeKind.LeadChange, null, nowUtc, settings.LeadChangeCooldownSeconds))
            {
                var lead = now.Standings[0];
                var second = now.Standings[1];
                var gap = lead.Score - second.Score;
                var from = prev.Leader is null ? "a tie" : prev.Leader.Value.Name();
                outp.Add(new Notice(
                    NoticeKind.LeadChange,
                    now.Leader,
                    $"{now.Leader.Value.Name()} takes the lead on {lead.Score:N0} ({gap:N0} clear of {second.Team.Name()}), up from {from}. New target: {now.Leader.Value.Name()} \u2014 deny their income, do not fight the team below you.",
                    Urgent: true));
            }
        }

        // --- Emerging threat: the team that will matter soon ---
        if (settings.AnnounceEmergingThreat && now.Threat is not null && !now.ThreatIsLeader)
        {
            var t = now.Threat.Value;
            var rate = now.Standings.First(s => s.Team == t).PerMinute;
            if (rate >= settings.SurgeRatePerMinute && TryCooldown(NoticeKind.EmergingThreat, t, nowUtc, settings.ThreatCooldownSeconds))
            {
                var msg = $"{t.Name()} is surging (+{rate:0}/min) and is the danger in the next few minutes.";
                if (now.EtaCatchLeaderMinutes.TryGetValue(t, out var eta) && eta is not null && now.Leader is not null)
                    msg += $" They overtake {now.Leader.Value.Name()} in about {Advisor.FormatMinutes(eta.Value)}.";
                outp.Add(new Notice(NoticeKind.EmergingThreat, t, msg, Urgent: false));
            }
        }

        // --- Win imminent: someone is about to cross the threshold ---
        if (settings.AnnounceWinImminent && now.Map is not null)
        {
            foreach (var team in Teams.All)
            {
                if (winImminentFired.Contains(team)) continue;
                if (!now.ToWin.TryGetValue(team, out var remaining)) continue;
                if (remaining <= 0 || remaining > settings.WinImminentMargin) continue;

                winImminentFired.Add(team);
                outp.Add(new Notice(
                    NoticeKind.WinImminent,
                    team,
                    $"{team.Name()} is {remaining:N0} from the win ({now.Map.Target:N0} on {now.Map.Campaign}). Interrupt their objective now \u2014 one ranged hit breaks a channel.",
                    Urgent: true));
            }
        }
    }

    private bool TryCooldown(NoticeKind kind, GrandCompanyTeam? team, DateTime nowUtc, int cooldownSeconds)
    {
        // A null team means "the whole kind": a lead change is throttled regardless of who grabbed it, so a
        // score seesaw cannot produce a burst of calls.
        var key = (kind, team ?? (GrandCompanyTeam)(-1));
        if (lastFired.TryGetValue(key, out var last) && (nowUtc - last).TotalSeconds < cooldownSeconds)
            return false;

        lastFired[key] = nowUtc;
        return true;
    }
}
