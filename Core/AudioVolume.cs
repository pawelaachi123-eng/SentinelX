using System.Runtime.InteropServices;

namespace SentinelX.Core;

/// <summary>0.95 · sterowanie głośnością wyjścia audio (Windows Core Audio, bez pakietów NuGet).
/// Wszystkie operacje zwracają null, gdy system nie udostępnia dźwięku — wtedy komenda
/// odpowiada uczciwie, że sterowanie jest niedostępne, zamiast udawać.</summary>
public static class AudioVolume
{
    /// <summary>Master volume 0–100, albo null gdy niedostępny.</summary>
    public static int? GetVolumePercent()
    {
        try
        {
            using var endpoint = Open();
            if (endpoint == null) return null;
            endpoint.Volume.GetMasterVolumeLevelScalar(out float level);
            return (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Ustawia głośność 0–100. true = ustawiono, null = niedostępne.</summary>
    public static bool? SetVolumePercent(int percent)
    {
        if (percent is < 0 or > 100) return false;
        try
        {
            using var endpoint = Open();
            if (endpoint == null) return null;
            endpoint.Volume.SetMasterVolumeLevelScalar(percent / 100f, IntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Czy wyciszone; null = niedostępne.</summary>
    public static bool? IsMuted()
    {
        try
        {
            using var endpoint = Open();
            if (endpoint == null) return null;
            endpoint.Volume.GetMute(out bool muted);
            return muted;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Wycisza lub przywraca dźwięk. true = zmieniono, null = niedostępne.</summary>
    public static bool? SetMuted(bool muted)
    {
        try
        {
            using var endpoint = Open();
            if (endpoint == null) return null;
            endpoint.Volume.SetMute(muted, IntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException) { return null; }
    }

    private static Endpoint? Open()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out IMMDevice device);
            if (device == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object volumeObj);
            return new Endpoint(device, (IAudioEndpointVolume)volumeObj);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private sealed class Endpoint : IDisposable
    {
        public IAudioEndpointVolume Volume { get; }
        private readonly IMMDevice device;
        public Endpoint(IMMDevice device, IAudioEndpointVolume volume) { this.device = device; Volume = volume; }
        public void Dispose()
        {
            try { Marshal.ReleaseComObject(Volume); } catch (ArgumentException) { }
            try { Marshal.ReleaseComObject(device); } catch (ArgumentException) { }
        }
    }

    // ---- COM interop (Windows Core Audio, układ vtable od Visty — stabilny) ----

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint channelCount);
        int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
        int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
        int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
