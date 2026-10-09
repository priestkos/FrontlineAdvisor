using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FrontlineAdvisor.Core;

/// <summary>The advisor's reading of the match, ready to render.</summary>
public sealed record Briefing(
    string Headline,
    string FocusTitle,
    string FocusBody,
    string ThreatTitle,
    string ThreatBody,
    string? PostureBody,
    string MapHeading,
    string MapNote);

/// <summary>
/// Turns an analysis into doctrine. Every line here traces to the Frontline guide:
/// play the leader, deny their rate, never fight the team just below you, your enemy is the clock/rate/stalemate.
/// Pure: no Dalamud types.
/// </summary>
public static class Advisor
{
    public static Briefing Build(MatchAnalysis a, GrandCompanyTeam? myTeam)
    {
        var (focusTitle, focusBody) = Focus(a, myTeam);
        var (threatTitle, threatBody) = Threat(a);

        return new Briefing(
            Headline(a),
            focusTitle,
            focusBody,
            threatTitle,
            threatBody,
            myTeam is null ? null : Posture(a, myTeam.Value),
            a.Map?.NoteHeading ?? "Map",
            a.Map?.Note ?? string.Empty);
    }

    public static string Headline(MatchAnalysis a)
    {
        if (a.Standings.Count == 0) return "Waiting for the first reading.";

        if (a.TiedLead)
        {
            var tied = a.Standings.Where(s => s.Score == a.Standings[0].Score)
                .Select(s => $"{s.Team.Name()} {s.Score:N0}");
            return $"Tied at the top: {string.Join(" / ", tied)}.";
        }

        var lead = a.Standings[0];
        var second = a.Standings[1];
        var gap = lead.Score - second.Score;
        var tail = gap == 0 ? "level with" : $"{gap:N0} ahead of";
        return $"{lead.Team.Name()} leads on {lead.Score:N0} \u2014 {tail} {second.Team.Name()} ({second.Score:N0}).";
    }

    private static (string Title, string Body) Focus(MatchAnalysis a, GrandCompanyTeam? myTeam)
    {
        if (a.TiedLead || a.FocusTarget is null)
            return ("FOCUS", "No single leader yet. Take the safe objective, farm Battle High on stragglers and keep the reserve uncommitted.");

        var target = a.FocusTarget.Value;
        var body = new List<string>();

        if (a.Leader is not null && target != a.Leader.Value)
            body.Add($"{target.Name()} will reach the target first even though {a.Leader.Value.Name()} holds the higher score \u2014 stop {target.Name()} now.");
        else
            body.Add($"Your target is the leader: {target.Name()}.");

        body.Add("Win by rating, not kills: deny their objectives, and pinch them only while they are already committed against the third company \u2014 never brawl them 1v2.");

        if (myTeam is not null)
        {
            var myRank = a.Rank(myTeam.Value);
            if (myRank == 1)
                body.Add("You are the leader: your enemy is the clock. Hold, stay unclumped and out of the open melee.");
            else if (myRank == 3)
                body.Add("You are 3rd: never fight the team just above you \u2014 you need them to bleed the leader. Pincer the leader instead.");
            else
                body.Add("You are 2nd: attack the leader's rate, and keep the third company alive as a distraction.");
        }

        return ($"FOCUS: {target.Name()}", string.Join(" ", body));
    }

    private static (string Title, string Body) Threat(MatchAnalysis a)
    {
        if (a.Threat is null)
            return ("THREAT SOON", "No one is scoring yet \u2014 the match is still settling. Nothing to fear this instant.");

        var t = a.Threat.Value;
        var rate = a.Standings.First(s => s.Team == t).PerMinute;
        var rateText = $"+{rate:0}/min";

        if (a.ThreatIsLeader)
            return ($"THREAT SOON: {t.Name()} pulling away",
                $"{t.Name()} is both leading and scoring fastest ({rateText}). A runaway leader is everyone's target \u2014 which is exactly why they charge their Limit Break 25% slower. Pinch them together.");

        var body = $"{t.Name()} is scoring fastest ({rateText}) and is the team to watch in the next few minutes.";
        if (a.EtaCatchLeaderMinutes.TryGetValue(t, out var eta) && eta is not null && a.Leader is not null)
            body += $" At this rate they overtake {a.Leader.Value.Name()} in about {FormatMinutes(eta.Value)} \u2014 break their income now.";

        return ($"THREAT SOON: {t.Name()}", body);
    }

    private static string Posture(MatchAnalysis a, GrandCompanyTeam myTeam)
    {
        var rank = a.Rank(myTeam);

        return rank switch
        {
            1 => "You lead. Your enemy is the clock: accumulate quietly, stay unclumped, take uncontested objectives and avoid the open melee \u2014 a visible runaway leader is a 2v1 waiting to happen.",
            2 => $"You are 2nd. Your enemy is {(a.Leader?.Name() ?? "1st")}'s rate: deny their objectives and pinch them while they fight the third company. Keep the 3rd alive \u2014 annihilating 1st for free just crowns 3rd.",
            _ => "You are 3rd. Your enemy is the stalemate: manufacture fights between the other two and sweep objectives while they grind. Pincer the leader \u2014 never the team just above you.",
        };
    }

    /// <summary>"45s" or "1m30s" - a rate estimate never deserves false precision.</summary>
    public static string FormatMinutes(double minutes)
    {
        if (double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes < 0) return "a while";
        var totalSeconds = (int)Math.Round(minutes * 60);
        if (totalSeconds < 60) return $"{totalSeconds}s";
        var m = totalSeconds / 60;
        var s = totalSeconds % 60;
        return s == 0 ? $"{m}m" : $"{m}m{s}s";
    }

    public static string FormatGap(int gap) => gap <= 0 ? "level" : $"-{gap.ToString("N0", CultureInfo.InvariantCulture)}";
}
