using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Audio;

/// <summary>A snapshot of one music track candidate from the music engine.</summary>
public sealed record MusicTrackSnapshot(
    string Name,
    string? Location,
    float Priority,
    bool IsActive,
    bool OnCooldown,
    long? CooldownEndsMs,
    bool ShouldPlay);

/// <summary>A snapshot of the music engine state.</summary>
public sealed record MusicSnapshot(
    string? CurrentTrackName,
    string? CurrentTrackLocation,
    bool CurrentActive,
    long MillisecondsSinceLastTrack,
    IReadOnlyList<MusicTrackSnapshot> Candidates);

/// <summary>
/// Reads the client's music engine state: current track, candidates, cooldowns, and active status.
/// Values are detached and dispatched on the client thread.
/// </summary>
public sealed class MusicDriver
{
    private static readonly FieldInfo? s_tracksOnCooldown = typeof(SystemMusicEngine)
        .GetField("tracksOnCooldown", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_shuffledTracks = typeof(SystemMusicEngine)
        .GetField("shuffledTracks", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_config = typeof(SystemMusicEngine)
        .GetField("config", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HeadlessClient _client;

    internal MusicDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads the current music engine state on the client thread.</summary>
    public MusicSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        // SystemMusicEngine is a ClientSystem, not ModSystem, so use reflection
        var systems = _client.Client.GetType()
            .GetField("clientSystems", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(_client.Client) as IEnumerable<object>;
        
        var engine = systems?.FirstOrDefault(s => s.GetType().Name == "SystemMusicEngine") as SystemMusicEngine;
        if (engine == null)
        {
            return new MusicSnapshot(null, null, false, 0, []);
        }

        var current = engine.CurrentTrack;
        var cooldowns = s_tracksOnCooldown?.GetValue(engine) as System.Collections.Concurrent.ConcurrentDictionary<IMusicTrack, long>;
        var shuffled = s_shuffledTracks?.GetValue(engine) as IMusicTrack[];
        var elapsed = _client.Client.ElapsedMilliseconds;

        var candidates = new List<MusicTrackSnapshot>();
        if (shuffled != null)
        {
            foreach (var track in shuffled)
            {
                long cooldownEnd = 0;
                bool onCooldown = cooldowns?.TryGetValue(track, out cooldownEnd) == true && cooldownEnd > elapsed;
                // ShouldPlay has multiple overloads; use the simplest check
                bool shouldPlay = !onCooldown && track.IsActive == false;
                candidates.Add(new MusicTrackSnapshot(
                    track.Name,
                    (track as MusicTrack)?.Location?.ToString(),
                    track.Priority,
                    track.IsActive,
                    onCooldown,
                    onCooldown ? cooldownEnd : null,
                    shouldPlay));
            }
        }

        return new MusicSnapshot(
            current?.Name,
            (current as MusicTrack)?.Location?.ToString(),
            engine.CurrentActive,
            engine.MillisecondsSinceLastTrack,
            candidates);
    });

    /// <summary>Whether any music is currently playing.</summary>
    public bool IsPlaying => Snapshot().CurrentActive;

    /// <summary>The name of the current track, or null if none.</summary>
    public string? CurrentTrackName => Snapshot().CurrentTrackName;

    /// <summary>Stops the current track with a fade out.</summary>
    public void Stop() => _client.RunOnClientThread(() =>
    {
        var systems = _client.Client.GetType()
            .GetField("clientSystems", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(_client.Client) as IEnumerable<object>;
        
        var engine = systems?.FirstOrDefault(s => s.GetType().Name == "SystemMusicEngine") as SystemMusicEngine;
        engine?.CurrentTrack?.FadeOut(1f);
    });
}
