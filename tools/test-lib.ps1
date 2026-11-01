# Shared helpers for test-run.ps1 / drive.ps1. ASCII only.
if (-not ('EndpointMute' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class EndpointMute {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out int c);
        [PreserveSig] int SetMasterVolumeLevel(float l, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float l, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float l);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float l);
        [PreserveSig] int SetChannelVolumeLevel(int ch, float l, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(int ch, float l, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(int ch, out float l);
        [PreserveSig] int GetChannelVolumeLevelScalar(int ch, out float l);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    static IAudioEndpointVolume Get() {
        var en = (IMMDeviceEnumerator)new Enumerator();
        IMMDevice d; en.GetDefaultAudioEndpoint(0, 1, out d);
        var iid = typeof(IAudioEndpointVolume).GUID; object o;
        d.Activate(ref iid, 23, IntPtr.Zero, out o);
        return (IAudioEndpointVolume)o;
    }
    public static bool GetMute() { bool m; Get().GetMute(out m); return m; }
    public static void SetMute(bool m) { var g = Guid.Empty; Get().SetMute(m, ref g); }
}
'@
}

function Set-TomlSection([string]$text, [string]$section, [string[]]$lines) {
    # replace the body of [section] (up to the next blank line before a '## ' or '[') with $lines
    $pattern = '(?ms)^\#?\[' + [regex]::Escape($section) + '\]\r?\n(?:(?!\r?\n\r?\n).)*'
    $body = '[' + $section + "]`r`n" + ($lines -join "`r`n")
    if ([regex]::IsMatch($text, $pattern)) { return [regex]::Replace($text, $pattern, $body, 1) }
    return $text + "`r`n" + $body + "`r`n"
}
