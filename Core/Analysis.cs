using System;
using System.Collections.Generic;
using System.Linq;

namespace FrontlineAdvisor.Core;

/// <summary>A single poll of all three team scores. Pure: linked by the offline harness.</summary>
public sealed record ScoreSample(DateTime TimeUtc, int Maelstrom, int TwinAdder, int ImmortalFlames)
{
    public int this[GrandCompanyTeam team] => team switch
    {
        GrandCompanyTeam.Maelstrom => Maelstrom,
        GrandCompanyTeam.TwinAdder => TwinAdder,
        GrandCompanyTeam.ImmortalFlames => ImmortalFlames,
        _ => 0,
    };

    public static ScoreSample Of(DateTime timeUtc, int maelstrom, int twinAdder, int immortalFlames)
        => new(timeUtc, maelstrom, twinAdder, immortalFlames);
}

/// <summary>A team's standing plus its recent scoring rate, in points per minute.</summary>
public sealed record TeamRate(GrandCompanyTeam Team, int Score, double PerMinute);

/// <summary>The whole picture at one instant.</summary>
public sealed record MatchAnalysis(
    DateTime TimeUtc,
    IReadOnlyList<TeamRate> Standings,
    GrandCompanyTeam? Leader,
    bool TiedLead,
    GrandCompanyTeam? FocusTarget,
    GrandCompanyTeam? ImminentWinner,
    GrandCompanyTeam? Threat,
    bool ThreatIsLeader,
    FrontlineMap? Map,
    IReadOnlyDictionary<GrandCompanyTeam, int> ToWin,
    IReadOnlyDictionary<GrandCompanyTeam, double?> EtaWinMinutes,
    IReadOnlyDictionary<GrandCompanyTeam, double?> EtaCatchLeaderMinutes)
{
    public TeamRate Row(GrandCompanyTeam team) => Standings.First(s => s.Team == team);

    /// <summary>Points behind the current leader (0 for the leader itself).</summary>
    public int Gap(GrandCompanyTeam team) => Standings[0].Score - Row(team).Score;

    /// <summary>1 for the leader, 2 and 3 downward; ties all share the better rank.</summary>
    public int Rank(GrandCompanyTeam team)
    {
        var top = Standings[0].Score;
        var score = Row(team).Score;
        if (score == top) return 1;
        return Standings.Count(s => s.Score > score) + 1;
    }
}

public static class Analyzer
{
    /// <summary>Below this, a window cannot produce a meaningful slope.</summary>
    public const double MinWindowSpanSeconds = 8.0;

    public static MatchAnalysis Analyze(
        ScoreSample current,
        IReadOnlyList<ScoreSample> history,
        FrontlineMap? map,
        double windowSeconds)
    {
        var rate = new Dictionary<GrandCompanyTeam, double>();
        foreach (var team in Teams.All)
            rate[team] = SlopePerMinute(history, team, current.TimeUtc, windowSeconds);

        var standings = Teams.All
            .Select(t => new TeamRate(t, current[t], rate[t]))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.PerMinute)
            .ThenBy(r => (int)r.Team)
            .ToList();

        var top = standings[0].Score;
        var topTeams = standings.Where(s => s.Score == top).Select(s => s.Team).ToList();
        var tied = topTeams.Count > 1;
        var leader = tied ? (GrandCompanyTeam?)null : topTeams[0];

        // The threat is whoever is scoring fastest; a tie goes to the higher score.
        var fastest = Teams.All
            .OrderByDescending(t => rate[t])
            .ThenByDescending(t => current[t])
            .First();
        var threatIsMeaningful = rate[fastest] >= 1.0;
        var threat = threatIsMeaningful ? fastest : (GrandCompanyTeam?)null;
        var threatIsLeader = threat is not null && threat == leader;

        var toWin = new Dictionary<GrandCompanyTeam, int>();
        var etaWin = new Dictionary<GrandCompanyTeam, double?>();
        var etaCatch = new Dictionary<GrandCompanyTeam, double?>();

        var leaderScore = standings[0].Score;
        var leaderRate = leader is null ? rate[topTeams[0]] : rate[leader.Value];

        foreach (var team in Teams.All)
        {
            var score = current[team];
            var r = rate[team];
            var remaining = map is null ? -1 : Math.Max(0, map.Target - score);

            toWin[team] = remaining;
            etaWin[team] = map is null || r <= 0.01 ? null : remaining / r;
            etaCatch[team] = leader is not null && team != leader.Value && r > leaderRate
                ? (leaderScore - score) / (r - leaderRate)
                : null;
        }

        // The imminent winner is whoever reaches the target first; the leader stands in when there is no map.
        GrandCompanyTeam? imminent = null;
        if (map is not null)
        {
            var candidates = Teams.All
                .Where(t => etaWin[t] is not null)
                .OrderBy(t => etaWin[t]!.Value)
                .ThenByDescending(t => current[t])
                .ToList();
            if (candidates.Count > 0) imminent = candidates[0];
        }

        imminent ??= leader;

        return new MatchAnalysis(
            current.TimeUtc,
            standings,
            leader,
            tied,
            FocusTarget: imminent,
            ImminentWinner: imminent,
            Threat: threat,
            ThreatIsLeader: threatIsLeader,
            Map: map,
            ToWin: toWin,
            EtaWinMinutes: etaWin,
            EtaCatchLeaderMinutes: etaCatch);
    }

    /// <summary>
    /// Least-squares slope of one team's score over the trailing window, in points per minute.
    /// Returns 0 when the window is too short or has no spread - a rate is never guessed.
    /// </summary>
    public static double SlopePerMinute(
        IReadOnlyList<ScoreSample> history,
        GrandCompanyTeam team,
        DateTime nowUtc,
        double windowSeconds)
    {
        if (history.Count < 2) return 0.0;

        var cutoff = nowUtc.AddSeconds(-windowSeconds);
        var pts = history.Where(s => s.TimeUtc >= cutoff).OrderBy(s => s.TimeUtc).ToList();
        // Too few points inside the configured window: do not invent a rate from older history.
        if (pts.Count < 2) return 0.0;

        var baseTicks = pts[0].TimeUtc.Ticks;
        var span = (pts[^1].TimeUtc.Ticks - baseTicks) / (double)TimeSpan.TicksPerSecond;
        if (span < MinWindowSpanSeconds) return 0.0;

        double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
        foreach (var s in pts)
        {
            var x = (s.TimeUtc.Ticks - baseTicks) / (double)TimeSpan.TicksPerSecond;
            var y = (double)s[team];
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
        }

        var n = pts.Count;
        var denom = n * sumXx - sumX * sumX;
        if (Math.Abs(denom) < 1e-9) return 0.0;

        return (n * sumXy - sumX * sumY) / denom * 60.0;
    }
}
