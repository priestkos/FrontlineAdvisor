using System;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using FrontlineAdvisor.Core;

namespace FrontlineAdvisor;

/// <summary>
/// Emits advisor notices as a loud chat line and (optionally) a screen toast.
/// Anything the game throws here is logged, never propagated - a notification must not take the session down.
/// </summary>
public sealed class Notifier(IChatGui chatGui, IToastGui toastGui, IPluginLog log)
{
    public void Emit(Notice notice, Configuration configuration)
    {
        try
        {
            if (configuration.ShowInChat)
            {
                var builder = new SeStringBuilder();
                builder.AddIcon(notice.Urgent ? BitmapFontIcon.GoldStar : BitmapFontIcon.GreenDot);
                builder.AddUiForeground(configuration.AccentColourKey);
                builder.AddText($" FRONTLINE {TagFor(notice.Kind)} ");
                builder.AddUiForegroundOff();
                builder.AddText(notice.Message);

                chatGui.Print(builder.Build(), "FLINE", configuration.AccentColourKey);
            }

            if (configuration.ShowToast)
            {
                toastGui.ShowNormal($"[Frontline] {notice.Message}", new ToastOptions
                {
                    Position = ToastPosition.Bottom,
                    Speed = notice.Urgent ? ToastSpeed.Fast : ToastSpeed.Slow,
                });
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "FrontlineAdvisor: failed to emit a notice");
        }
    }

    private static string TagFor(NoticeKind kind) => kind switch
    {
        NoticeKind.LeadChange => "LEAD CHANGE",
        NoticeKind.FirstLead => "FIRST LEAD",
        NoticeKind.EmergingThreat => "THREAT",
        NoticeKind.WinImminent => "WIN CLOSE",
        _ => "NOTICE",
    };
}
