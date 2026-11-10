using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    public enum SingleInstanceActivationKind
    {
        None = 0,
        ShowMainWindow = 1,
        ReopenSettings = 2,
        ReopenOptimizer = 3,
        Shutdown = 4
    }

    internal sealed class SingleInstanceCoordinator : IDisposable
    {
        public const string DefaultScopeId = "WinThumbsPreloader_SingleInstance_v3_CF49DD18-AA76-4E79-97C2-4FEAED1AED5F";

        private const uint EVENT_MODIFY_STATE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenEventW(uint dwDesiredAccess, bool bInheritHandle, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static readonly object LocalScopesLock = new();
        private static readonly HashSet<string> ActiveLocalScopes = new(StringComparer.OrdinalIgnoreCase);

        private readonly string _scopeId;
        private readonly Action<SingleInstanceActivationKind> _onActivate;
        private Mutex _mutex;
        private bool _ownsMutex;
        private EventWaitHandle _activateEvent;
        private EventWaitHandle _reopenSettingsEvent;
        private EventWaitHandle _reopenOptimizerEvent;
        private EventWaitHandle _shutdownEvent;
        private RegisteredWaitHandle _activateWaitReg;
        private RegisteredWaitHandle _reopenSettingsWaitReg;
        private RegisteredWaitHandle _reopenOptimizerWaitReg;
        private RegisteredWaitHandle _shutdownWaitReg;
        private bool _disposed;

        public bool IsPrimaryInstance => _ownsMutex;
        public string ScopeId => _scopeId;

        private SingleInstanceCoordinator(
            string scopeId,
            Mutex mutex,
            bool ownsMutex,
            Action<SingleInstanceActivationKind> onActivate)
        {
            _scopeId = scopeId;
            _mutex = mutex;
            _ownsMutex = ownsMutex;
            _onActivate = onActivate;

            if (_ownsMutex)
            {
                InitializeWaitHandles();
            }
        }

        private static string GetMutexName(string scopeId) => $"Local\\{scopeId}_Mutex";
        private static string GetActivateEventName(string scopeId) => $"Local\\{scopeId}_Activate";
        private static string GetReopenSettingsEventName(string scopeId) => $"Local\\{scopeId}_ReopenSettings";
        private static string GetReopenOptimizerEventName(string scopeId) => $"Local\\{scopeId}_ReopenOptimizer";
        private static string GetShutdownEventName(string scopeId) => $"Local\\{scopeId}_Shutdown";

        public static string GetEffectiveScopeId(string scopeId = null)
        {
            if (!string.IsNullOrWhiteSpace(scopeId) && IsSafeScopeIdentifier(scopeId))
                return scopeId.Trim();

            string envScope = Environment.GetEnvironmentVariable("WTP_SINGLE_INSTANCE_SCOPE");
            if (!string.IsNullOrWhiteSpace(envScope) && IsSafeScopeIdentifier(envScope))
                return envScope.Trim();

            return DefaultScopeId;
        }

        private static bool IsSafeScopeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 80)
                return false;

            if (!trimmed.StartsWith("WinThumbsPreloader_", StringComparison.Ordinal) &&
                !trimmed.StartsWith("WTP_", StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool isAsciiAlphaNum =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9');

                if (!isAsciiAlphaNum && c != '_' && c != '-')
                    return false;
            }

            return true;
        }

        public static SingleInstanceCoordinator TryAcquire(
            string scopeId = null,
            Action<SingleInstanceActivationKind> onActivate = null)
        {
            scopeId = GetEffectiveScopeId(scopeId);

            lock (LocalScopesLock)
            {
                if (ActiveLocalScopes.Contains(scopeId))
                {
                    return null;
                }

                string mutexName = GetMutexName(scopeId);
                Mutex mutex = CreatePermissiveMutex(mutexName, out _);

                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
                catch (Exception ex)
                {
                    WriteLine("Failed to wait on single-instance mutex: " + ex.Message, LoggingFrequency.DebugLogging);
                    mutex.Dispose();
                    return null;
                }

                if (!acquired)
                {
                    mutex.Dispose();
                    return null;
                }

                ActiveLocalScopes.Add(scopeId);
                return new SingleInstanceCoordinator(scopeId, mutex, ownsMutex: true, onActivate);
            }
        }

        public static bool TryAcquireOrNotifyExisting(
            Options options,
            out SingleInstanceCoordinator coordinator,
            Action<SingleInstanceActivationKind> onActivate,
            string scopeId = null)
        {
            scopeId = GetEffectiveScopeId(scopeId);
            coordinator = TryAcquire(scopeId, onActivate);
            if (coordinator != null)
            {
                return true;
            }

            // Another GUI instance is already active in this user session.
            if (options != null && options.startMinimized)
            {
                WriteLine("Existing GUI instance detected; ignoring duplicate -startminimized launch.", LoggingFrequency.GUILogging);
                return false;
            }

            SingleInstanceActivationKind kind = SingleInstanceActivationKind.ShowMainWindow;
            if (options != null)
            {
                if (options.reopenSettings)
                    kind = SingleInstanceActivationKind.ReopenSettings;
                else if (options.reopenOptimizer)
                    kind = SingleInstanceActivationKind.ReopenOptimizer;
            }

            SignalExistingInstance(kind, scopeId);
            return false;
        }

        public static bool SignalExistingInstance(
            SingleInstanceActivationKind kind,
            string scopeId = null)
        {
            scopeId = GetEffectiveScopeId(scopeId);

            if (kind != SingleInstanceActivationKind.Shutdown)
            {
                GrantForegroundPermissionToPeerInstances();
            }

            string eventName = kind switch
            {
                SingleInstanceActivationKind.ReopenSettings => GetReopenSettingsEventName(scopeId),
                SingleInstanceActivationKind.ReopenOptimizer => GetReopenOptimizerEventName(scopeId),
                SingleInstanceActivationKind.Shutdown => GetShutdownEventName(scopeId),
                _ => GetActivateEventName(scopeId)
            };

            try
            {
                IntPtr eventHandle = OpenEventW(EVENT_MODIFY_STATE, false, eventName);
                if (eventHandle == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    if (!SetEvent(eventHandle))
                    {
                        return false;
                    }
                }
                finally
                {
                    CloseHandle(eventHandle);
                }

                WriteLine($"Signaled existing instance with {kind}.", LoggingFrequency.GUILogging);
                return true;
            }
            catch (Exception ex)
            {
                WriteLine($"Failed to signal existing instance ({kind}): " + ex.Message, LoggingFrequency.GUILogging);
                return false;
            }
        }

        private static void GrantForegroundPermissionToPeerInstances()
        {
            try
            {
                int currentPid = Environment.ProcessId;
                foreach (Process peer in Process.GetProcessesByName("WinThumbsPreloader"))
                {
                    using (peer)
                    {
                        if (peer.Id != currentPid)
                        {
                            try { AllowSetForegroundWindow(peer.Id); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // Best effort for Win32 foreground transfer
            }
        }

        public static bool SignalShutdown(string scopeId = null)
        {
            return SignalExistingInstance(SingleInstanceActivationKind.Shutdown, scopeId);
        }

        private void InitializeWaitHandles()
        {
            try
            {
                _activateEvent = CreateOrOpenPermissiveEvent(GetActivateEventName(_scopeId));
                _reopenSettingsEvent = CreateOrOpenPermissiveEvent(GetReopenSettingsEventName(_scopeId));
                _reopenOptimizerEvent = CreateOrOpenPermissiveEvent(GetReopenOptimizerEventName(_scopeId));
                _shutdownEvent = CreateOrOpenPermissiveEvent(GetShutdownEventName(_scopeId));

                _activateWaitReg = ThreadPool.RegisterWaitForSingleObject(
                    _activateEvent,
                    (_, timedOut) =>
                    {
                        if (!timedOut && !_disposed)
                            _onActivate?.Invoke(SingleInstanceActivationKind.ShowMainWindow);
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);

                _reopenSettingsWaitReg = ThreadPool.RegisterWaitForSingleObject(
                    _reopenSettingsEvent,
                    (_, timedOut) =>
                    {
                        if (!timedOut && !_disposed)
                            _onActivate?.Invoke(SingleInstanceActivationKind.ReopenSettings);
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);

                _reopenOptimizerWaitReg = ThreadPool.RegisterWaitForSingleObject(
                    _reopenOptimizerEvent,
                    (_, timedOut) =>
                    {
                        if (!timedOut && !_disposed)
                            _onActivate?.Invoke(SingleInstanceActivationKind.ReopenOptimizer);
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);

                _shutdownWaitReg = ThreadPool.RegisterWaitForSingleObject(
                    _shutdownEvent,
                    (_, timedOut) =>
                    {
                        if (!timedOut && !_disposed)
                            _onActivate?.Invoke(SingleInstanceActivationKind.Shutdown);
                    },
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
            }
            catch (Exception ex)
            {
                WriteLine("Failed to initialize single-instance wait handles: " + ex.Message, LoggingFrequency.DebugLogging);
            }
        }

        private static Mutex CreatePermissiveMutex(string name, out bool createdNew)
        {
            try
            {
                var security = new MutexSecurity();
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                security.AddAccessRule(new MutexAccessRule(admins, MutexRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new MutexAccessRule(system, MutexRights.FullControl, AccessControlType.Allow));

                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity.User != null)
                {
                    security.AddAccessRule(new MutexAccessRule(
                        identity.User,
                        MutexRights.Synchronize | MutexRights.Modify,
                        AccessControlType.Allow));
                }

                return MutexAcl.Create(initiallyOwned: false, name, out createdNew, security);
            }
            catch
            {
                return new Mutex(initiallyOwned: false, name, out createdNew);
            }
        }

        private static EventWaitHandle CreateOrOpenPermissiveEvent(string name)
        {
            try
            {
                var security = new EventWaitHandleSecurity();
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                security.AddAccessRule(new EventWaitHandleAccessRule(admins, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new EventWaitHandleAccessRule(system, EventWaitHandleRights.FullControl, AccessControlType.Allow));

                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity.User != null)
                {
                    security.AddAccessRule(new EventWaitHandleAccessRule(
                        identity.User,
                        EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                        AccessControlType.Allow));
                }

                return EventWaitHandleAcl.Create(
                    initialState: false,
                    EventResetMode.AutoReset,
                    name,
                    out _,
                    security);
            }
            catch
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, name);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try { _activateWaitReg?.Unregister(null); } catch { }
            try { _reopenSettingsWaitReg?.Unregister(null); } catch { }
            try { _reopenOptimizerWaitReg?.Unregister(null); } catch { }
            try { _shutdownWaitReg?.Unregister(null); } catch { }

            _activateWaitReg = null;
            _reopenSettingsWaitReg = null;
            _reopenOptimizerWaitReg = null;
            _shutdownWaitReg = null;

            try { _activateEvent?.Dispose(); } catch { }
            try { _reopenSettingsEvent?.Dispose(); } catch { }
            try { _reopenOptimizerEvent?.Dispose(); } catch { }
            try { _shutdownEvent?.Dispose(); } catch { }

            _activateEvent = null;
            _reopenSettingsEvent = null;
            _reopenOptimizerEvent = null;
            _shutdownEvent = null;

            lock (LocalScopesLock)
            {
                ActiveLocalScopes.Remove(_scopeId);

                if (_mutex != null)
                {
                    if (_ownsMutex)
                    {
                        try { _mutex.ReleaseMutex(); } catch { }
                        _ownsMutex = false;
                    }
                    try { _mutex.Dispose(); } catch { }
                    _mutex = null;
                }
            }
        }
    }
}
