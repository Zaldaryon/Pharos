using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Audio;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class NullAudioDeviceTests
{
    static NullAudioDeviceTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void Environment_Should_HaveAlsoftDriversConfiguredAsNull()
    {
        string? drivers = Environment.GetEnvironmentVariable("ALSOFT_DRIVERS");
        Assert.Equal("null", drivers);
    }

    [Fact]
    public void NullLoadedSound_Should_SupportAllPlaybackAndFadingOperationsSafely()
    {
        SoundParams soundParams = new(new AssetLocation("game:sounds/test.ogg"))
        {
            Volume = 0.5f,
            Pitch = 1.0f
        };

        using NullLoadedSound sound = new(soundParams);

        Assert.NotNull(sound.Params);
        Assert.Equal(1, sound.Channels);
        Assert.Equal(0f, sound.SoundLengthSeconds);
        Assert.False(sound.IsDisposed);
        Assert.False(sound.IsPlaying);
        Assert.True(sound.HasStopped);
        Assert.True(sound.IsReady);

        // Playback state transitions
        sound.Start();
        Assert.True(sound.IsPlaying);
        Assert.False(sound.IsPaused);
        Assert.False(sound.HasStopped);

        sound.Pause();
        Assert.True(sound.IsPaused);
        Assert.False(sound.IsPlaying);
        Assert.False(sound.HasStopped);

        sound.Stop();
        Assert.False(sound.IsPlaying);
        Assert.False(sound.IsPaused);
        Assert.True(sound.HasStopped);

        sound.Toggle(true);
        Assert.True(sound.IsPlaying);
        sound.Toggle(false);
        Assert.False(sound.IsPlaying);

        // Property adjustments
        sound.SetPitch(1.5f);
        Assert.Equal(1.5f, sound.Params.Pitch);

        sound.SetVolume(0.75f);
        Assert.Equal(0.75f, sound.Params.Volume);

        sound.SetPosition(new Vec3f(10f, 20f, 30f));
        Assert.Equal(10f, sound.Params.Position.X);
        Assert.Equal(20f, sound.Params.Position.Y);
        Assert.Equal(30f, sound.Params.Position.Z);

        sound.SetPosition(1f, 2f, 3f);
        Assert.Equal(1f, sound.Params.Position.X);

        sound.SetLooping(true);
        Assert.True(sound.Params.ShouldLoop);

        // Fading callbacks
        bool fadeInvoked = false;
        sound.FadeTo(0.2, 1.0f, s => fadeInvoked = true);
        Assert.True(fadeInvoked);
        Assert.Equal(0.2f, sound.Params.Volume);

        bool fadeInInvoked = false;
        sound.FadeIn(0.5f, s => fadeInInvoked = true);
        Assert.True(fadeInInvoked);
        Assert.Equal(1.0f, sound.Params.Volume);

        bool fadeOutInvoked = false;
        sound.FadeOut(0.5f, s => fadeOutInvoked = true);
        Assert.True(fadeOutInvoked);
        Assert.Equal(0.0f, sound.Params.Volume);

        sound.FadeOutAndStop(0.5f);
        Assert.True(sound.HasStopped);

        // Disposal
        sound.Dispose();
        Assert.True(sound.IsDisposed);
        Assert.False(sound.IsPlaying);
    }

    [Fact]
    public void NullAudioPatcher_Should_InterceptClientPlatformAudioCalls()
    {
        NullAudioPatcher.Patch();
        Assert.True(NullAudioPatcher.IsPatched);

        ClientLogger logger = new();
        ClientPlatformWindows platform = new(logger);

        // 1. StartAudio should be a safe no-op
        platform.StartAudio();

        // 2. Audio properties should not throw NullReferenceException
        Assert.Equal(0f, platform.MasterSoundLevel);
        platform.MasterSoundLevel = 0.5f;

        Assert.Contains("Null Audio Device", platform.AvailableAudioDevices);
        Assert.Equal("Null Audio Device", platform.CurrentAudioDevice);
        platform.CurrentAudioDevice = "Null Audio Device";

        // 3. Listener update should be a safe no-op
        platform.UpdateAudioListener(0f, 0f, 0f, 0f, 0f, -1f);

        // 4. CreateAudioData should return dummy AudioMetaData with Loaded = 2
        IAsset dummyAsset = new DummyAsset();
        AudioData createdData = platform.CreateAudioData(dummyAsset);
        Assert.NotNull(createdData);
        Assert.Equal(2, createdData.Loaded);
        Assert.IsType<AudioMetaData>(createdData);

        // 5. CreateAudio (2-parameter and 3-parameter overloads) should return NullLoadedSound
        SoundParams soundParams = new(new AssetLocation("game:sounds/test.ogg"));
        AudioMetaData dummyData = new(dummyAsset);
        ILoadedSound loadedSound = platform.CreateAudio(soundParams, dummyData);

        Assert.NotNull(loadedSound);
        Assert.IsType<NullLoadedSound>(loadedSound);
        loadedSound.Start();
        Assert.True(loadedSound.IsPlaying);
        loadedSound.Dispose();

        ILoadedSound loadedSound3 = platform.CreateAudio(soundParams, dummyData, null!);
        Assert.NotNull(loadedSound3);
        Assert.IsType<NullLoadedSound>(loadedSound3);
        loadedSound3.Start();
        Assert.True(loadedSound3.IsPlaying);
        loadedSound3.Dispose();
    }

    [Fact]
    public void NullAudioPatcher_Should_SupportPatchAndUnpatchCycle()
    {
        NullAudioPatcher.Patch();
        Assert.True(NullAudioPatcher.IsPatched);

        NullAudioPatcher.Unpatch();
        Assert.False(NullAudioPatcher.IsPatched);

        NullAudioPatcher.Patch();
        Assert.True(NullAudioPatcher.IsPatched);
    }

    [Fact]
    public void HeadlessClientBootstrap_Should_BootWithZeroSoundLevelsAndNullAudio()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true,
            UseNullAudioDevice = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.NotNull(client);
        Assert.True(NullAudioPatcher.IsPatched);

        Assert.Equal(0, ClientSettings.MasterSoundLevel);
        Assert.Equal(0, ClientSettings.SoundLevel);
        Assert.Equal(0, ClientSettings.EntitySoundLevel);
        Assert.Equal(0, ClientSettings.AmbientSoundLevel);
        Assert.Equal(0, ClientSettings.WeatherSoundLevel);
        Assert.Equal(0, ClientSettings.MusicLevel);
    }

    private class DummyAsset : IAsset
    {
        public string Name => "dummy.ogg";
        public AssetLocation Location { get; set; } = new("game:sounds/dummy.ogg");
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public IAssetOrigin Origin { get; set; } = null!;
        public bool IsPatched { get; set; }
        public bool IsLoaded() => true;
        public string ToText() => string.Empty;
        public BitmapRef ToBitmap(ICoreClientAPI capi) => null!;
        public T ToObject<T>(Newtonsoft.Json.JsonSerializerSettings? settings = null) => default!;
    }
}
