using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Audio;

/// <summary>
/// Intercepts Vintage Story client platform audio operations using Harmony to prevent OpenAL hardware initialization.
/// </summary>
public static class NullAudioPatcher
{
    private const string HarmonyId = "zaldaryon.pharos.audio";
    private static readonly Harmony _harmony = new(HarmonyId);
    private static readonly object _patchLock = new();
    private static bool _isPatched;

    public static bool IsPatched
    {
        get
        {
            lock (_patchLock)
            {
                return _isPatched;
            }
        }
    }

    /// <summary>
    /// Applies Harmony patches to ClientPlatformWindows to neutralize all OpenAL audio calls.
    /// </summary>
    public static void Patch()
    {
        lock (_patchLock)
        {
            if (_isPatched)
            {
                return;
            }

            Type platformType = typeof(ClientPlatformWindows);

            // 1. StartAudio
            MethodInfo? startAudio = platformType.GetMethod("StartAudio", BindingFlags.Public | BindingFlags.Instance);
            if (startAudio != null)
            {
                _harmony.Patch(startAudio, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_StartAudio)));
            }

            // 2. CreateAudioData(IAsset)
            MethodInfo? createAudioData = platformType.GetMethod("CreateAudioData", BindingFlags.Public | BindingFlags.Instance, [typeof(IAsset)]);
            if (createAudioData != null)
            {
                _harmony.Patch(createAudioData, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_CreateAudioData)));
            }

            // 3. CreateAudio(SoundParams, AudioData)
            MethodInfo? createAudio2 = platformType.GetMethod("CreateAudio", BindingFlags.Public | BindingFlags.Instance, [typeof(SoundParams), typeof(AudioData)]);
            if (createAudio2 != null)
            {
                _harmony.Patch(createAudio2, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_CreateAudio)));
            }

            // 4. CreateAudio(SoundParams, AudioData, ClientMain)
            MethodInfo? createAudio3 = platformType.GetMethod("CreateAudio", BindingFlags.Public | BindingFlags.Instance, [typeof(SoundParams), typeof(AudioData), typeof(ClientMain)]);
            if (createAudio3 != null)
            {
                _harmony.Patch(createAudio3, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_CreateAudioWithGame)));
            }

            // 5. UpdateAudioListener
            MethodInfo? updateListener = platformType.GetMethod("UpdateAudioListener", BindingFlags.Public | BindingFlags.Instance, [typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float)]);
            if (updateListener != null)
            {
                _harmony.Patch(updateListener, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_UpdateAudioListener)));
            }

            // 6. MasterSoundLevel getter/setter
            PropertyInfo? masterSoundLevelProp = platformType.GetProperty("MasterSoundLevel", BindingFlags.Public | BindingFlags.Instance);
            if (masterSoundLevelProp?.GetGetMethod() is { } getMasterSoundLevel)
            {
                _harmony.Patch(getMasterSoundLevel, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_GetMasterSoundLevel)));
            }
            if (masterSoundLevelProp?.GetSetMethod() is { } setMasterSoundLevel)
            {
                _harmony.Patch(setMasterSoundLevel, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_SetMasterSoundLevel)));
            }

            // 7. AvailableAudioDevices getter
            PropertyInfo? availableDevicesProp = platformType.GetProperty("AvailableAudioDevices", BindingFlags.Public | BindingFlags.Instance);
            if (availableDevicesProp?.GetGetMethod() is { } getAvailableDevices)
            {
                _harmony.Patch(getAvailableDevices, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_GetAvailableAudioDevices)));
            }

            // 8. CurrentAudioDevice getter/setter
            PropertyInfo? currentDeviceProp = platformType.GetProperty("CurrentAudioDevice", BindingFlags.Public | BindingFlags.Instance);
            if (currentDeviceProp?.GetGetMethod() is { } getCurrentDevice)
            {
                _harmony.Patch(getCurrentDevice, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_GetCurrentAudioDevice)));
            }
            if (currentDeviceProp?.GetSetMethod() is { } setCurrentDevice)
            {
                _harmony.Patch(setCurrentDevice, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_SetCurrentAudioDevice)));
            }

            // 9. AddAudioSettingsWatchers
            MethodInfo? addWatchers = platformType.GetMethod("AddAudioSettingsWatchers", BindingFlags.Public | BindingFlags.Instance);
            if (addWatchers != null)
            {
                _harmony.Patch(addWatchers, prefix: new HarmonyMethod(typeof(NullAudioPatcher), nameof(Prefix_AddAudioSettingsWatchers)));
            }

            _isPatched = true;
        }
    }

    /// <summary>
    /// Removes all Harmony patches applied by NullAudioPatcher.
    /// </summary>
    public static void Unpatch()
    {
        lock (_patchLock)
        {
            if (!_isPatched)
            {
                return;
            }

            _harmony.UnpatchAll(HarmonyId);
            _isPatched = false;
        }
    }

    private static bool Prefix_StartAudio() => false;

    private static bool Prefix_CreateAudioData(IAsset asset, ref AudioData __result)
    {
        AudioMetaData meta = new(asset)
        {
            Pcm = Array.Empty<byte>(),
            Channels = 1,
            Rate = 44100,
            BitsPerSample = 16,
            Loaded = 2
        };
        __result = meta;
        return false;
    }

    private static bool Prefix_CreateAudio(SoundParams sound, AudioData data, ref ILoadedSound __result)
    {
        __result = new NullLoadedSound(sound);
        return false;
    }

    private static bool Prefix_CreateAudioWithGame(SoundParams sound, AudioData data, ClientMain game, ref ILoadedSound __result)
    {
        __result = new NullLoadedSound(sound);
        return false;
    }

    private static bool Prefix_UpdateAudioListener() => false;

    private static bool Prefix_GetMasterSoundLevel(ref float __result)
    {
        __result = 0f;
        return false;
    }

    private static bool Prefix_SetMasterSoundLevel() => false;

    private static bool Prefix_GetAvailableAudioDevices(ref IList<string> __result)
    {
        __result = new List<string> { "Null Audio Device" };
        return false;
    }

    private static bool Prefix_GetCurrentAudioDevice(ref string __result)
    {
        __result = "Null Audio Device";
        return false;
    }

    private static bool Prefix_SetCurrentAudioDevice() => false;

    private static bool Prefix_AddAudioSettingsWatchers() => false;
}
