using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClientDashboard;

public sealed class AudioMuteManager
{
    private const int CLSCTX_ALL = 23;
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public sealed class AudioMuteDebugInfo
    {
        public int TrackedPids { get; set; }
        public int? ControlledPid { get; set; }
        public bool MuteEnabled { get; set; }
        public int EndpointsScanned { get; set; }
        public int SessionsScanned { get; set; }
        public int SessionsMatched { get; set; }
        public int SessionsMuteTrue { get; set; }
        public int SessionsMuteFalse { get; set; }
        public int SetMuteErrors { get; set; }
        public string Note { get; set; } = "ok";
    }

    public AudioMuteDebugInfo ApplyMuteState(HashSet<int> trackedClientPids, int? controlledPid, bool muteAllExceptControlled, HashSet<int>? forcedMutePids = null)
    {
        AudioMuteDebugInfo result = new()
        {
            TrackedPids = trackedClientPids.Count,
            ControlledPid = controlledPid,
            MuteEnabled = muteAllExceptControlled
        };
        Exception? threadEx = null;

        var t = new Thread(() =>
        {
            try
            {
                result = ApplyMuteStateCore(trackedClientPids, controlledPid, muteAllExceptControlled, forcedMutePids);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        })
        {
            IsBackground = true
        };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        if (!t.Join(2000))
        {
            result.Note = "timeout";
            return result;
        }
        if (threadEx != null)
        {
            result.Note = $"{threadEx.GetType().Name}: {Trim(threadEx.Message, 120)}";
        }
        return result;
    }

    private AudioMuteDebugInfo ApplyMuteStateCore(HashSet<int> trackedClientPids, int? controlledPid, bool muteAllExceptControlled, HashSet<int>? forcedMutePids)
    {
        var debug = new AudioMuteDebugInfo
        {
            TrackedPids = trackedClientPids.Count,
            ControlledPid = controlledPid,
            MuteEnabled = muteAllExceptControlled
        };

        var trackedNames = BuildTrackedProcessNames(trackedClientPids);
        bool enableJavaFallback = muteAllExceptControlled ||
                                  trackedNames.Contains("java") ||
                                  trackedNames.Contains("javaw");

        object? enumObj = null;
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? endpoint = null;
        string stage = "start";
        try
        {
            stage = "Type.GetTypeFromCLSID";
            var enumType = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator, throwOnError: true);
            stage = "Activator.CreateInstance";
            enumObj = Activator.CreateInstance(enumType!);
            if (enumObj is null)
            {
                debug.Note = "enumerator-null";
                return debug;
            }

            stage = "QI IMMDeviceEnumerator";
            deviceEnumerator = QueryInterface<IMMDeviceEnumerator>(enumObj);
            if (deviceEnumerator == null)
            {
                debug.Note = "qi-IMMDeviceEnumerator-failed";
                return debug;
            }

            stage = "GetDefaultAudioEndpoint";
            int hr = deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out endpoint);
            if (hr != 0 || endpoint == null)
            {
                debug.Note = $"GetDefaultAudioEndpoint hr={hr}";
                return debug;
            }
            debug.EndpointsScanned = 1;

            IAudioSessionManager2? sessionManager = null;
            IAudioSessionEnumerator? sessionEnumerator = null;
            try
            {
                Guid iidAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
                stage = "IMMDevice.Activate(IAudioSessionManager2)";
                if (endpoint.Activate(ref iidAudioSessionManager2, CLSCTX_ALL, IntPtr.Zero, out object? managerObj) != 0 ||
                    managerObj == null)
                {
                    debug.Note = "activate-session-manager-failed";
                    return debug;
                }

                stage = "QI IAudioSessionManager2";
                sessionManager = QueryInterface<IAudioSessionManager2>(managerObj);
                SafeReleaseComObject(managerObj);
                if (sessionManager == null)
                {
                    debug.Note = "qi-session-manager-failed";
                    return debug;
                }

                stage = "IAudioSessionManager2.GetSessionEnumerator";
                if (sessionManager.GetSessionEnumerator(out sessionEnumerator) != 0 || sessionEnumerator == null)
                {
                    debug.Note = "get-session-enumerator-failed";
                    return debug;
                }
                stage = "IAudioSessionEnumerator.GetCount";
                if (sessionEnumerator.GetCount(out int sessionCount) != 0)
                {
                    debug.Note = "get-session-count-failed";
                    return debug;
                }

                for (int i = 0; i < sessionCount; i++)
                {
                    IAudioSessionControl? sessionControl = null;
                    IAudioSessionControl2? sessionControl2 = null;
                    ISimpleAudioVolume? volume = null;
                    try
                    {
                        stage = "IAudioSessionEnumerator.GetSession";
                        if (sessionEnumerator.GetSession(i, out sessionControl) != 0 || sessionControl == null)
                            continue;
                        debug.SessionsScanned++;

                        stage = "QI IAudioSessionControl2";
                        sessionControl2 = QueryInterface<IAudioSessionControl2>(sessionControl);
                        if (sessionControl2 == null)
                            continue;
                        stage = "IAudioSessionControl2.GetProcessId";
                        if (sessionControl2.GetProcessId(out uint pid) != 0)
                            continue;
                        int processId = (int)pid;

                        bool isTracked = trackedClientPids.Contains(processId);
                        if (!isTracked && enableJavaFallback)
                        {
                            var procName = TryGetProcessName(processId);
                            if (procName != null &&
                                (procName.Equals("java", StringComparison.OrdinalIgnoreCase) ||
                                 procName.Equals("javaw", StringComparison.OrdinalIgnoreCase)))
                            {
                                isTracked = true;
                            }

                            if (!isTracked)
                            {
                                _ = sessionControl2.GetDisplayName(out string displayName);
                                _ = sessionControl2.GetSessionIdentifier(out string sessionId);
                                string combined = $"{displayName} {sessionId}";
                                if (combined.IndexOf("openjdk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    combined.IndexOf("java", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    isTracked = true;
                                }
                            }
                        }

                        if (!isTracked)
                            continue;

                        debug.SessionsMatched++;
                        stage = "QI ISimpleAudioVolume";
                        volume = QueryInterface<ISimpleAudioVolume>(sessionControl);
                        if (volume == null)
                            continue;

                        bool manuallyMuted = forcedMutePids != null && forcedMutePids.Contains(processId);
                        bool shouldMute = manuallyMuted || (muteAllExceptControlled && (!controlledPid.HasValue || processId != controlledPid.Value));
                        Guid eventContext = Guid.Empty;
                        stage = "ISimpleAudioVolume.SetMute";
                        int muteHr = volume.SetMute(shouldMute, ref eventContext);
                        if (muteHr == 0)
                        {
                            if (shouldMute)
                                debug.SessionsMuteTrue++;
                            else
                                debug.SessionsMuteFalse++;
                        }
                        else
                        {
                            debug.SetMuteErrors++;
                            debug.Note = $"SetMute hr={muteHr}";
                        }
                    }
                    finally
                    {
                        SafeReleaseComObject(volume);
                        SafeReleaseComObject(sessionControl2);
                        SafeReleaseComObject(sessionControl);
                    }
                }
            }
            finally
            {
                SafeReleaseComObject(sessionEnumerator);
                SafeReleaseComObject(sessionManager);
            }

            if (debug.Note == "ok" || string.IsNullOrWhiteSpace(debug.Note))
                debug.Note = "ok";
        }
        catch (Exception ex)
        {
            debug.Note = $"{ex.GetType().Name}@{stage}: {Trim(ex.Message, 120)}";
            return debug;
        }
        finally
        {
            SafeReleaseComObject(endpoint);
            SafeReleaseComObject(deviceEnumerator);
            SafeReleaseComObject(enumObj);
        }

        return debug;
    }

    private static HashSet<string> BuildTrackedProcessNames(HashSet<int> pids)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in pids)
        {
            var name = TryGetProcessName(pid);
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        return names;
    }

    private static string? TryGetProcessName(int pid)
    {
        if (pid <= 0)
            return null;
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static T? QueryInterface<T>(object comObject) where T : class
    {
        IntPtr unk = IntPtr.Zero;
        IntPtr ppv = IntPtr.Zero;
        try
        {
            unk = Marshal.GetIUnknownForObject(comObject);
            Guid iid = typeof(T).GUID;
            int hr = Marshal.QueryInterface(unk, ref iid, out ppv);
            if (hr != 0 || ppv == IntPtr.Zero)
                return null;
            return Marshal.GetObjectForIUnknown(ppv) as T;
        }
        finally
        {
            if (ppv != IntPtr.Zero) Marshal.Release(ppv);
            if (unk != IntPtr.Zero) Marshal.Release(unk);
        }
    }

    private static void SafeReleaseComObject(object? obj)
    {
        if (obj == null)
            return;
        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.ReleaseComObject(obj);
        }
        catch
        {
            // ignore
        }
    }

    private static string Trim(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Length <= maxLen)
            return value;
        return value.Substring(0, maxLen);
    }

    private enum EDataFlow
    {
        eRender = 0
    }

    private enum ERole
    {
        eMultimedia = 1
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        int GetDevice(string id, out IMMDevice device);
        int RegisterEndpointNotificationCallback(IntPtr client);
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(int stgmAccess, out IntPtr properties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetState(out int state);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);
        int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        int RegisterSessionNotification(IntPtr sessionNotification);
        int UnregisterSessionNotification(IntPtr sessionNotification);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);
        int GetSession(int sessionCount, out IAudioSessionControl session);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    private interface IAudioSessionControl
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingId);
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    private interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
        int GetGroupingParam(out Guid groupingId);
        int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        int RegisterAudioSessionNotification(IntPtr client);
        int UnregisterAudioSessionNotification(IntPtr client);
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        int GetProcessId(out uint processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid eventContext);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
        int GetMute(out bool isMuted);
    }
}
