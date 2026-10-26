using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MusicHonorific;

/// <summary>
/// 查「谁真的在出声」。用 Windows 自带的音频会话（CoreAudio），
/// 不需要任何外部依赖，也不联网。
/// </summary>
public static class AudioSessions
{
    private const int CLSCTX_ALL = 23;
    private const int E_RENDER = 0;        // 播放设备
    private const int E_MULTIMEDIA = 1;    // 默认多媒体角色
    private const float SILENCE = 0.0005f; // 峰值低于这个就当没在放

    /// <summary>返回当前正在出声的进程 PID 集合。</summary>
    public static HashSet<uint> GetPlayingProcessIds()
    {
        var playing = new HashSet<uint>();

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(E_RENDER, E_MULTIMEDIA, out device) < 0 || device is null)
                return playing;

            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj) < 0 || mgrObj is null)
                return playing;

            manager = (IAudioSessionManager2)mgrObj;
            if (manager.GetSessionEnumerator(out sessions) < 0 || sessions is null)
                return playing;

            if (sessions.GetCount(out var count) < 0)
                return playing;

            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    if (sessions.GetSession(i, out control) < 0 || control is null)
                        continue;

                    var control2 = (IAudioSessionControl2)control;
                    if (control2.GetProcessId(out var pid) < 0 || pid == 0)
                        continue;

                    var meter = (IAudioMeterInformation)control;
                    if (meter.GetPeakValue(out var peak) < 0)
                        continue;

                    if (peak > SILENCE)
                        playing.Add(pid);
                }
                catch
                {
                    // 单个会话读不到就跳过
                }
                finally
                {
                    if (control is not null && Marshal.IsComObject(control))
                        Marshal.ReleaseComObject(control);
                }
            }
        }
        catch
        {
            // 整条链路失败就当作「查不到」，调用方会退回顺序匹配
        }
        finally
        {
            if (sessions is not null && Marshal.IsComObject(sessions)) Marshal.ReleaseComObject(sessions);
            if (manager is not null && Marshal.IsComObject(manager)) Marshal.ReleaseComObject(manager);
            if (device is not null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }

        return playing;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(ref Guid audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid audioSessionGuid, int streamFlags, out IntPtr audioVolume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? sessionEnum);
        [PreserveSig] int RegisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr sessionNotification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int sessionCount);
        [PreserveSig] int GetSession(int sessionIndex, out IAudioSessionControl? session);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr retVal);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr retVal);
        [PreserveSig] int GetProcessId(out uint retVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int channelCount);
        [PreserveSig] int GetChannelsPeakValues(int channelCount, [Out] float[] peakValues);
        [PreserveSig] int QueryHardwareSupport(out int hardwareSupportMask);
    }
}
