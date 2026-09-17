using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Audio;

/// <summary>
/// A null, no-op implementation of <see cref="ILoadedSound"/> used to bypass OpenAL hardware requirements.
/// </summary>
public sealed class NullLoadedSound : ILoadedSound
{
    private readonly SoundParams _soundParams;
    private bool _disposed;
    private bool _isPlaying;
    private bool _isPaused;
    private float _playbackPosition;

    public NullLoadedSound(SoundParams? soundParams = null)
    {
        _soundParams = soundParams ?? new SoundParams();
    }

    public float SoundLengthSeconds => 0f;

    public float PlaybackPosition
    {
        get => _playbackPosition;
        set => _playbackPosition = value;
    }

    public bool IsDisposed => _disposed;

    public bool IsPlaying => _isPlaying;

    public bool IsFadingIn => false;

    public bool IsFadingOut => false;

    public bool HasStopped => _disposed || (!_isPlaying && !_isPaused);

    public int Channels => 1;

    public SoundParams Params => _soundParams;

    public bool IsPaused => _isPaused;

    public bool IsReady => true;

    public void Start()
    {
        _isPlaying = true;
        _isPaused = false;
    }

    public void Stop()
    {
        _isPlaying = false;
        _isPaused = false;
    }

    public void Pause()
    {
        _isPaused = true;
        _isPlaying = false;
    }

    public void Toggle(bool on)
    {
        if (on)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    public void SetPitch(float val)
    {
        if (_soundParams != null)
        {
            _soundParams.Pitch = val;
        }
    }

    public void SetPitchOffset(float val)
    {
    }

    public void SetVolume(float val)
    {
        if (_soundParams != null)
        {
            _soundParams.Volume = val;
        }
    }

    public void SetVolume()
    {
    }

    public void SetPosition(Vec3f position)
    {
        if (_soundParams != null)
        {
            _soundParams.Position = position;
        }
    }

    public void SetPosition(float x, float y, float z)
    {
        if (_soundParams != null)
        {
            _soundParams.Position = new Vec3f(x, y, z);
        }
    }

    public void SetLooping(bool on)
    {
        if (_soundParams != null)
        {
            _soundParams.ShouldLoop = on;
        }
    }

    public void FadeTo(double newVolume, float duration, Action<ILoadedSound>? onFaded)
    {
        if (_soundParams != null)
        {
            _soundParams.Volume = (float)newVolume;
        }
        onFaded?.Invoke(this);
    }

    public void FadeOut(float seconds, Action<ILoadedSound>? onFadedOut)
    {
        FadeTo(0.0, seconds, onFadedOut);
    }

    public void FadeIn(float seconds, Action<ILoadedSound>? onFadedIn)
    {
        FadeTo(1.0, seconds, onFadedIn);
    }

    public void FadeOutAndStop(float seconds)
    {
        Stop();
    }

    public void SetLowPassfiltering(float value)
    {
    }

    public void SetReverb(float reverbDecayTime)
    {
    }

    public bool HasReverbStopped(long elapsedMilliseconds) => true;

    public void Dispose()
    {
        _disposed = true;
        _isPlaying = false;
        _isPaused = false;
    }
}
