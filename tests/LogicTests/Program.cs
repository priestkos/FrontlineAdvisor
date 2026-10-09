using System;
using System.Collections.Generic;
using System.Linq;
using FrontlineAdvisor.Core;

/// <summary>
/// Runnable checks for the Frontline Advisor logic. The score-reading layer needs the game, but everything
/// that decides who is leading, who to focus and who is the threat runs here with no Dalamud and no client.
///
/// Run: dotnet run --project tests/LogicTests -c Release
/// </summary>
internal static class Program
{
    private static int passed;
    private static int failed;

    private static readonly DateTime Base = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private static int Main()
    {
        Sections.MapsSection();
        Sections.TeamsSection();
        Sections.AnalyzerSection();
        Sections.AdvisorSection();
        Sections.TrackerSection();
        Sections.FullMatch();

        Console.WriteLine();
        Console.WriteLine($"  {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    // ---------------- helpers ----------------

    internal static void Check(bool condition, string label)
    {
        if (condition)
        {
            passed++;
            Console.WriteLine($"  ok   {label}");
        }
        else
        {
            failed++;
            Console.WriteLine($"  FAIL {label}");
        }
    }

    internal static void Equal<T>(T actual, T expected, string label)
        => Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{label} (expected {expected}, got {actual})");

    internal static void Contains(string haystack, string needle, string label)
        => Check(haystack.Contains(needle, StringComparison.OrdinalIgnoreCase), $"{label} [\"{needle}\" in \"{haystack}\"]");

    internal static ScoreSample At(double seconds, int mael, int adder, int flame)
        => ScoreSample.Of(Base.AddSeconds(seconds), mael, adder, flame);

    internal static List<ScoreSample> Ramp(double from, double to, double step, int m0, int a0, int f0, int mRate, int aRate, int fRate)
    {
        var list = new List<ScoreSample>();
        for (var t = from; t <= to + 1e-9; t += step)
        {
            var minutes = (t - from) / 60.0;
            list.Add(At(t, m0 + (int)Math.Round(mRate * minutes), a0 + (int)Math.Round(aRate * minutes), f0 + (int)Math.Round(fRate * minutes)));
        }
        return list;
    }

    private static class Sections
    {
        internal static void MapsSection()
        {
            Console.WriteLine("MapCatalog");
            Equal(MapCatalog.Maps.Count, 5, "five Frontline maps");
            Equal(MapCatalog.ByDutyId(127)?.Campaign, "Secure", "duty 127 is Secure");
            Equal(MapCatalog.ByDutyId(130)?.Target, 2000, "Seize target is 2,000");
            Equal(MapCatalog.ByDutyId(180)?.Campaign, "Shatter", "duty 180 is Shatter");
            Equal(MapCatalog.ByDutyId(701)?.Target, 1400, "Onsal target is 1,400");
            Equal(MapCatalog.ByDutyId(1080)?.Campaign, "Triumph", "duty 1080 is Triumph");
            Equal(MapCatalog.ByDutyId(9999), null, "unknown duty id resolves to nothing");

            // The guide's headline numbers.
            Equal(MapCatalog.ByName("Secure")?.Target, 2400, "Secure target is 2,400");
            Equal(MapCatalog.ByName("Shatter")?.Target, 1600, "Shatter target is 1,600");
            Equal(MapCatalog.ByName("Danshig Naadam")?.Target, 1400, "Naadam target is 1,400");
            Equal(MapCatalog.ByName("onsal")?.Campaign, "Danshig Naadam", "fuzzy match on zone fragment");
            Equal(MapCatalog.ByName("auto"), null, "auto is not a map");

            Check(MapCatalog.Maps.All(m => m.Note.Length > 40), "every map carries a playbook note");
        }

        internal static void TeamsSection()
        {
            Console.WriteLine("Teams");
            Equal(Teams.All.Count, 3, "three teams");
            Check(Teams.TryParse("mael", out var t1) && t1 == GrandCompanyTeam.Maelstrom, "parse mael");
            Check(Teams.TryParse("Twin Adder", out var t2) && t2 == GrandCompanyTeam.TwinAdder, "parse twin adder");
            Check(Teams.TryParse("flames", out var t3) && t3 == GrandCompanyTeam.ImmortalFlames, "parse flames");
            Check(!Teams.TryParse("garuda", out _), "reject nonsense");
            Equal(GrandCompanyTeam.TwinAdder.Name(), "Twin Adder", "display name");
        }

        internal static void AnalyzerSection()
        {
            Console.WriteLine("Analyzer");

            // A clean leader with no movement.
            var flat = Ramp(0, 120, 3, 500, 400, 300, 0, 0, 0);
            var a = Analyzer.Analyze(flat[^1], flat, MapCatalog.ByName("Secure"), 90);
            Equal(a.Leader, GrandCompanyTeam.Maelstrom, "flat leader");
            Check(!a.TiedLead, "not tied");
            Equal(a.Gap(GrandCompanyTeam.TwinAdder), 100, "gap to 2nd");
            Equal(a.Rank(GrandCompanyTeam.ImmortalFlames), 3, "3rd rank");
            Equal(a.ToWin[GrandCompanyTeam.Maelstrom], 1900, "points to the win");
            Check(a.Threat is null, "no threat while nobody scores");

            // A tie at the top.
            var tie = new List<ScoreSample> { At(0, 300, 300, 100), At(60, 300, 300, 100) };
            var at = Analyzer.Analyze(tie[^1], tie, MapCatalog.ByName("Secure"), 90);
            Check(at.TiedLead, "tied lead detected");
            Equal(at.Leader, null, "no single leader on a tie");
            Equal(at.FocusTarget, null, "no focus target on a tie");

            // Rate: +60 points per minute.
            var ramp = Ramp(0, 120, 3, 100, 100, 100, 60, 0, 0);
            var r = Analyzer.Analyze(ramp[^1], ramp, MapCatalog.ByName("Secure"), 90);
            Check(Math.Abs(r.Row(GrandCompanyTeam.Maelstrom).PerMinute - 60) < 3, $"rate measured near 60/min (got {r.Row(GrandCompanyTeam.Maelstrom).PerMinute:0.0})");
            Check(Math.Abs(r.Row(GrandCompanyTeam.TwinAdder).PerMinute) < 1, "flat team measures ~0/min");

            // An emerging team that is not the leader: 2nd is scoring fast, 1st is static.
            var surge = Ramp(0, 90, 3, 900, 500, 300, 0, 120, 0);
            var s = Analyzer.Analyze(surge[^1], surge, MapCatalog.ByName("Secure"), 90);
            Equal(s.Leader, GrandCompanyTeam.Maelstrom, "leader is still Maelstrom");
            Equal(s.Threat, GrandCompanyTeam.TwinAdder, "threat is the fastest scorer");
            Check(!s.ThreatIsLeader, "the threat is not the leader");
            Equal(s.FocusTarget, GrandCompanyTeam.TwinAdder, "focus follows the imminent winner");
            Check(s.EtaCatchLeaderMinutes[GrandCompanyTeam.TwinAdder] is not null, "an overtake ETA is produced");

            // The fastest scorer being the leader means "pulling away".
            var runaway = Ramp(0, 120, 3, 900, 700, 400, 120, 30, 0);
            var rw = Analyzer.Analyze(runaway[^1], runaway, MapCatalog.ByName("Secure"), 90);
            Check(rw.ThreatIsLeader, "leader as fastest scorer reads as pulling away");

            // No map: no target, no ETAs, focus falls back to the leader.
            var noMap = Analyzer.Analyze(ramp[^1], ramp, null, 90);
            Equal(noMap.ToWin[GrandCompanyTeam.Maelstrom], -1, "no map means no target");
            Check(noMap.EtaWinMinutes[GrandCompanyTeam.Maelstrom] is null, "no map means no win ETA");
            Equal(noMap.FocusTarget, GrandCompanyTeam.Maelstrom, "focus falls back to the leader");
        }

        internal static void AdvisorSection()
        {
            Console.WriteLine("Advisor");

            var ramp = Ramp(0, 90, 3, 900, 500, 300, 0, 120, 0);
            var a = Analyzer.Analyze(ramp[^1], ramp, MapCatalog.ByName("Secure"), 90);

            var brief = Advisor.Build(a, null);
            Contains(brief.Headline, "Maelstrom leads", "headline names the leader");
            Contains(brief.FocusTitle, "Twin Adder", "focus names the imminent winner");
            Contains(brief.FocusBody, "deny their objectives", "focus body carries the doctrine");
            Contains(brief.ThreatTitle, "Twin Adder", "threat names the fastest scorer");
            Contains(brief.ThreatBody, "overtake", "threat body estimates the overtake");
            Check(brief.PostureBody is null, "no posture without a known team");
            Contains(brief.MapNote, "4+ nodes", "Secure map note is the node-ring playbook");

            // Posture switches with our rank.
            var asLeader = Advisor.Build(a, GrandCompanyTeam.Maelstrom);
            Contains(asLeader.PostureBody!, "enemy is the clock", "1st place posture");
            var asThird = Advisor.Build(a, GrandCompanyTeam.ImmortalFlames);
            Contains(asThird.PostureBody!, "stalemate", "3rd place posture");
            var asSecond = Advisor.Build(a, GrandCompanyTeam.TwinAdder);
            Contains(asSecond.PostureBody!, "rate", "2nd place posture");

            Equal(Advisor.FormatMinutes(0.5), "30s", "format under a minute");
            Equal(Advisor.FormatMinutes(2.0), "2m", "format whole minutes");
            Equal(Advisor.FormatMinutes(2.5), "2m30s", "format minutes and seconds");
        }

        internal static void TrackerSection()
        {
            Console.WriteLine("Tracker");

            var settings = NotificationSettings.Default with { GraceSeconds = 20, RateWindowSeconds = 90 };
            var map = MapCatalog.ByName("Secure");

            // Grace: nothing fires while the match settles.
            var tracker = new MatchTracker();
            var notices = new List<Notice>();
            for (var t = 0; t <= 12; t += 3)
                notices.AddRange(tracker.Observe(At(t, 0, 0, 0), map, settings));
            Equal(notices.Count, 0, "grace suppresses everything");

            // First lead announced once, right after grace.
            var tracker2 = new MatchTracker();
            notices = new List<Notice>();
            notices.AddRange(tracker2.Observe(At(0, 10, 20, 5), map, settings));
            notices.AddRange(tracker2.Observe(At(25, 40, 20, 5), map, settings));
            Check(notices.Any(n => n.Kind == NoticeKind.FirstLead), "first lead fires after grace");
            notices.Clear();
            notices.AddRange(tracker2.Observe(At(30, 60, 20, 5), map, settings));
            Check(!notices.Any(n => n.Kind == NoticeKind.FirstLead), "first lead fires only once");

            // Lead change: the explicit ask.
            var tracker3 = new MatchTracker();
            notices = new List<Notice>();
            notices.AddRange(tracker3.Observe(At(0, 100, 50, 10), map, settings));
            notices.AddRange(tracker3.Observe(At(25, 100, 50, 10), map, settings)); // baseline
            notices.AddRange(tracker3.Observe(At(30, 180, 200, 10), map, settings)); // Adders overtake
            var change = notices.FirstOrDefault(n => n.Kind == NoticeKind.LeadChange);
            Check(change is not null, "a lead change fires");
            Equal(change?.Team, GrandCompanyTeam.TwinAdder, "the notice names the new leader");
            Contains(change!.Message, "takes the lead", "the notice reads like a call");

            // Cooldown: a flip-flop within the cooldown window is suppressed.
            notices = new List<Notice>();
            notices.AddRange(tracker3.Observe(At(32, 300, 200, 10), map, settings)); // Maelstrom back
            Check(!notices.Any(n => n.Kind == NoticeKind.LeadChange), "lead change respects its cooldown");

            // Emerging threat fires for a fast non-leader.
            var tracker4 = new MatchTracker();
            notices = new List<Notice>();
            for (var t = 0.0; t <= 100; t += 3)
                notices.AddRange(tracker4.Observe(At(t, 0, 0, 0), map, settings)); // settle + baseline
            notices.Clear();
            for (var t = 103.0; t <= 200; t += 3)
            {
                var minutes = (t - 100) / 60.0;
                var adder = (int)Math.Round(120 * minutes);
                notices.AddRange(tracker4.Observe(At(t, 500, adder, 0), map, settings));
            }
            Check(notices.Any(n => n.Kind == NoticeKind.EmergingThreat && n.Team == GrandCompanyTeam.TwinAdder), "emerging threat fires for the fast non-leader");

            // Win imminent fires when a team is within the margin.
            var tracker5 = new MatchTracker();
            notices = new List<Notice>();
            notices.AddRange(tracker5.Observe(At(0, 2200, 100, 50), map, settings));
            notices.AddRange(tracker5.Observe(At(25, 2300, 100, 50), map, settings)); // baseline
            notices.AddRange(tracker5.Observe(At(28, 2300, 100, 50), map, settings)); // now the checks run
            Check(notices.Any(n => n.Kind == NoticeKind.WinImminent && n.Team == GrandCompanyTeam.Maelstrom), "win-close fires inside the margin");
        }

        internal static void FullMatch()
        {
            Console.WriteLine("Full match (20 minutes, 1s poll)");

            var settings = NotificationSettings.Default;
            var map = MapCatalog.ByName("Danshig Naadam");
            var tracker = new MatchTracker();
            var notices = new List<Notice>();

            // Maelstrom leads early; Twin Adder surges past at ~8:40; Maelstrom answers and closes on 1,400.
            for (var t = 0.0; t <= 1200; t += 1)
            {
                double mael, adder;
                if (t < 400)
                {
                    mael = t * 1.2;
                    adder = t * 0.6;
                }
                else if (t < 800)
                {
                    mael = 480 + (t - 400) * 0.2;
                    adder = 240 + (t - 400) * 2.2;
                }
                else
                {
                    mael = 560 + (t - 800) * 2.4;
                    adder = 1120 + (t - 800) * 0.4;
                }

                var flame = t * 0.2;

                notices.AddRange(tracker.Observe(At(t, (int)mael, (int)adder, (int)flame), map, settings));
            }

            var changes = notices.Where(n => n.Kind == NoticeKind.LeadChange).ToList();
            Check(changes.Count >= 2, $"at least two lead changes in a turnaround match (got {changes.Count})");
            Check(changes.Any(n => n.Team == GrandCompanyTeam.TwinAdder), "the Twin Adder takeover was called");
            Check(changes.Any(n => n.Team == GrandCompanyTeam.Maelstrom), "the Maelstrom answer was called");
            Check(notices.Any(n => n.Kind == NoticeKind.WinImminent), "somebody closing on 1,400 was flagged");
            Check(tracker.Latest is not null && tracker.Latest.Standings.Count == 3, "the final analysis carries all three teams");
        }
    }
}
