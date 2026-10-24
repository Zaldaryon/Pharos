using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VerifyTests;

namespace Zaldaryon.Pharos.Verify;

/// <summary>Per-verification game version naming and explicit runtime-value scrubbing.</summary>
public static class VerifyExtensions
{
    /// <summary>Uses the actual game version in the snapshot name, for example Test.1.22.7.verified.txt.</summary>
    /// <param name="task">An unawaited Verify call.</param>
    /// <param name="methodName">Defaults to the calling test's name. Supply it when using a custom method name.</param>
    public static SettingsTask UniqueForGameVersion(this SettingsTask task, [CallerMemberName] string methodName = "")
    {
        task.CurrentSettings.UniqueForGameVersion(methodName);
        return task;
    }

    /// <summary>Uses the calling test name and installed game version. Does not change its directory or parameters.</summary>
    public static void UniqueForGameVersion(this VerifySettings settings, [CallerMemberName] string methodName = "") =>
        settings.UseMethodName($"{methodName}.{PharosVerify.GameVersion.Suffix()}");

    /// <summary>
    /// Scrubs only the supplied data-file ports. Pass the scenario's DataFilePorts. Other numbers stay visible.
    /// </summary>
    public static SettingsTask ScrubDataFilePorts(this SettingsTask task, IReadOnlyDictionary<string, int> ports)
    {
        task.CurrentSettings.ScrubDataFilePorts(ports);
        return task;
    }

    /// <summary>Copies the port map into this verification; no global registration or cross-scenario leakage.</summary>
    public static void ScrubDataFilePorts(this VerifySettings settings, IReadOnlyDictionary<string, int> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var replacements = ports.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        foreach (var pair in replacements)
        {
            if (pair.Value is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(ports));
        }
        settings.AddScrubber(builder =>
        {
            var text = builder.ToString();
            foreach (var pair in replacements)
            {
                string port = pair.Value.ToString(CultureInfo.InvariantCulture);
                text = Regex.Replace(text, @"(?<![\w.\-])" + port + @"(?![\w.\-])", _ => "{Port:" + pair.Key + "}", RegexOptions.CultureInvariant);
            }
            builder.Clear().Append(text);
        });
    }

    /// <summary>Scrubs an explicitly known runtime value, such as a player UID, entity ID or calendar date string.</summary>
    /// <remarks>Use a stable name. This affects only the current verification, not other tests.</remarks>
    public static SettingsTask ScrubGameValue(this SettingsTask task, string value, string name)
    {
        task.CurrentSettings.ScrubGameValue(value, name);
        return task;
    }

    /// <summary>Scrubs a known runtime token with word boundaries, without a global ID or date heuristic.</summary>
    public static void ScrubGameValue(this VerifySettings settings, string value, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        ArgumentException.ThrowIfNullOrEmpty(name);
        settings.AddScrubber(builder =>
        {
            string result = Regex.Replace(builder.ToString(), @"(?<!\w)" + Regex.Escape(value) + @"(?!\w)", _ => "{" + name + "}", RegexOptions.CultureInvariant);
            builder.Clear().Append(result);
        });
    }
}
