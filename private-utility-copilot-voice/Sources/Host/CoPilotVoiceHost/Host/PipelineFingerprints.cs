using System.Text;
using CoPilotVoiceHost.Core;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.Host;

/// <summary>Apply/Reload fingerprint helpers (grammar vs pipeline vs catalog). No I/O.</summary>
public static class PipelineFingerprints
{
    public static string SpeechGrammar(
        AppSettings settings,
        string profileName,
        CommandCatalog catalog,
        IReadOnlyList<ChecklistDefinition> checklists)
    {
        var sb = new StringBuilder(256);
        sb.Append(settings.Speech.WakeWord ?? "").Append('\u001f');
        sb.Append(settings.Speech.Culture ?? "").Append('\u001f');
        sb.Append(settings.Speech.Engine ?? "").Append('\u001f');
        sb.Append(profileName).Append('\u001f');
        sb.Append(Catalog(catalog)).Append('\u001f');
        sb.Append(Checklists(checklists, profileName));
        return sb.ToString();
    }

    public static string PipelineServices(string speechKey, AppSettings settings)
    {
        var sb = new StringBuilder(speechKey.Length + 128);
        sb.Append(speechKey).Append('\u001f');
        sb.Append(settings.Speech.PttKey ?? "").Append('\u001f');
        sb.Append(settings.Speech.PttGraceMs).Append('\u001f');
        sb.Append(settings.Speech.ContinuousListen).Append('\u001f');
        sb.Append(settings.Speech.ConfidenceThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('\u001f');
        sb.Append(settings.Tts.Engine ?? "").Append('\u001f');
        sb.Append(settings.Tts.Voice ?? "").Append('\u001f');
        sb.Append(settings.Tts.Rate).Append('\u001f');
        sb.Append(settings.Tts.Volume).Append('\u001f');
        sb.Append(settings.Tts.VoicePack ?? "").Append('\u001f');
        sb.Append(settings.Behavior.RequirePositiveClimbForGearUp).Append('\u001f');
        sb.Append(settings.Behavior.ConfirmBeforeAction).Append('\u001f');
        sb.Append(settings.Behavior.CalloutDelayMs);
        return sb.ToString();
    }

    public static string Catalog(CommandCatalog catalog)
    {
        var cmds = catalog.Commands;
        var sb = new StringBuilder(cmds.Count * 24);
        sb.Append(cmds.Count);
        foreach (var c in cmds.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|').Append(c.Id).Append(':').Append(c.Phrases.Count);
            if (c.Phrases.Count > 0)
                sb.Append(':').Append(c.Phrases[0]);
        }

        return sb.ToString();
    }

    public static string Checklists(IReadOnlyList<ChecklistDefinition> checklists, string profileName)
    {
        var list = ChecklistCatalog.ForProfile(checklists, profileName);
        var sb = new StringBuilder(list.Count * 24);
        sb.Append(list.Count);
        foreach (var c in list.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|').Append(c.Id).Append(':').Append(c.Phrases.Count);
            if (c.Phrases.Count > 0)
                sb.Append(':').Append(c.Phrases[0]);
            sb.Append('#').Append(c.Items.Count);
        }

        return sb.ToString();
    }
}
