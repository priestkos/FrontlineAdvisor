using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace FrontlineAdvisor;

/// <summary>
/// A read-only overlay on the Frontline instance-content director.
///
/// The three team scores and the local player's Battle High live at fixed offsets inside the director
/// object that <see cref="EventFramework.GetInstanceContentDirector"/> returns. The offsets match the ones a
/// maintained Frontline statistics plugin (PvpStats) reads against the same Dalamud build, and every read is
/// range-checked before it is trusted - a stale offset fails the sanity check rather than showing a wrong number.
/// This is a pure read: no signatures, no hooks, nothing is written back to the game.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal unsafe struct FrontlineScores
{
    private const int Base = 0x24D2;

    [FieldOffset(Base + 0x000)] public short Maelstrom;
    [FieldOffset(Base + 0x020)] public short TwinAdder;
    [FieldOffset(Base + 0x040)] public short ImmortalFlames;
    [FieldOffset(Base + 0xEE5)] public byte PlayerBattleHigh;
}

internal readonly record struct DirectorSnapshot(
    bool Ok,
    int Maelstrom,
    int TwinAdder,
    int ImmortalFlames,
    int BattleHigh,
    uint DutyId,
    int ContentTimeMax,
    string? Note)
{
    public static DirectorSnapshot Unavailable(uint dutyId, int timeMax, string note)
        => new(false, 0, 0, 0, 0, dutyId, timeMax, note);
}

/// <summary>Reads the live Frontline scores. Read-only, defensive, never throws into the game loop.</summary>
internal static unsafe class FrontlineReader
{
    private const int SanityCap = 9999;

    internal static nint DirectorAddress()
    {
        var framework = EventFramework.Instance();
        if (framework == null) return 0;
        return (nint)framework->GetInstanceContentDirector();
    }

    internal static uint CurrentDutyId()
    {
        var gameMain = GameMain.Instance();
        return gameMain == null ? 0u : (uint)gameMain->CurrentContentFinderConditionId;
    }

    internal static int ContentTimeMax(nint director)
    {
        if (director == 0) return 0;
        return (int)((InstanceContentDirector*)director)->ContentTimeMax;
    }

    /// <summary>
    /// Reads all three scores. When the values fail the range check the result is not Ok, so the caller can
    /// show "no reading" instead of a wrong scoreboard (and /fla dump lets the offset be re-derived).
    /// </summary>
    internal static DirectorSnapshot Read()
    {
        var director = DirectorAddress();
        var dutyId = CurrentDutyId();
        var timeMax = ContentTimeMax(director);

        if (director == 0)
            return DirectorSnapshot.Unavailable(dutyId, timeMax, "no instance-content director");

        var scores = (FrontlineScores*)director;
        var mael = scores->Maelstrom;
        var adder = scores->TwinAdder;
        var flame = scores->ImmortalFlames;

        if (mael < 0 || adder < 0 || flame < 0 || mael > SanityCap || adder > SanityCap || flame > SanityCap)
            return DirectorSnapshot.Unavailable(dutyId, timeMax,
                "scores out of range - the score offset may need re-deriving for this patch");

        return new DirectorSnapshot(true, mael, adder, flame, scores->PlayerBattleHigh, dutyId, timeMax, null);
    }
}
