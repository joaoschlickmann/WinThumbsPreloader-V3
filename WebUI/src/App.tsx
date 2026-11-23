import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge, subscribe } from './lib/bridge';
import type {
  BootstrapData,
  CacheProgressEvent,
  CacheResetEvent,
  CacheSnapshot,
  CodecsStateDto,
  OptimizerStateDto,
  PreloadPathValidationItem,
  ScheduleState,
  UpdateProgressEvent,
  UpdateStatusDto,
  ViewId,
} from './lib/types';
import { useTheme } from './hooks/useTheme';
import { Shell } from './components/Shell';
import { UpdateModal } from './components/UpdateModal';
import { LiveAnnouncer } from './components/ui/LiveAnnouncer';
import { ToastRegion, type ToastItem } from './components/ui/ToastRegion';
import { OverviewView } from './views/OverviewView';
import { PreloadView } from './views/PreloadView';
import { CacheView } from './views/CacheView';
import { ScheduleView } from './views/ScheduleView';
import { CodecsView } from './views/CodecsView';
import { OptimizeView } from './views/OptimizeView';
import { SettingsView } from './views/SettingsView';

const VIEW_TITLES: Record<ViewId, string> = {
  overview: 'Overview | WinThumbsPreloader',
  preload: 'Preload | WinThumbsPreloader',
  cache: 'Cache Manager | WinThumbsPreloader',
  schedule: 'Schedule | WinThumbsPreloader',
  codecs: 'Thumbnail Codecs | WinThumbsPreloader',
  optimizer: 'Windows Optimizer | WinThumbsPreloader',
  settings: 'Settings | WinThumbsPreloader',
};

export function App() {
  const [bootstrap, setBootstrap] = useState<BootstrapData | null>(null);
  const [activeView, setActiveView] = useState<ViewId>('overview');
  const [cacheSnapshot, setCacheSnapshot] = useState<CacheSnapshot | null>(null);
  const [cacheProgress, setCacheProgress] = useState<CacheProgressEvent | null>(
    null
  );
  const [codecsState, setCodecsState] = useState<CodecsStateDto | null>(null);
  const [optimizerState, setOptimizerState] =
    useState<OptimizerStateDto | null>(null);
  const [scheduleState, setScheduleState] = useState<ScheduleState | null>(null);
  const [updateStatus, setUpdateStatus] = useState<UpdateStatusDto | null>(null);
  const [updateProgress, setUpdateProgress] =
    useState<UpdateProgressEvent | null>(null);
  const [updateModalOpen, setUpdateModalOpen] = useState(false);

  const [preloadQueue, setPreloadQueue] = useState<PreloadPathValidationItem[]>([]);
  const [launchWarningDismissed, setLaunchWarningDismissed] = useState(false);

  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const [politeMessage, setPoliteMessage] = useState('');
  const [assertiveMessage, setAssertiveMessage] = useState('');
  const progressClearTimerRef = useRef<ReturnType<typeof setTimeout> | null>(
    null
  );
  const toastTimersRef = useRef<Map<string, ReturnType<typeof setTimeout>>>(
    new Map()
  );

  const { themeMode, effectiveTheme, systemTheme, setTheme, syncBootstrapTheme } =
    useTheme();

  const notify = useCallback(
    (tone: 'info' | 'warning' | 'error', message: string) => {
      const id = `${Date.now()}-${Math.random().toString(16).slice(2)}`;
      setToasts((prev) => [...prev.slice(-3), { id, tone, message }]);
      if (tone === 'error') {
        setAssertiveMessage(message);
      } else {
        setPoliteMessage(message);
      }
      const timerId = setTimeout(() => {
        toastTimersRef.current.delete(id);
        setToasts((prev) => prev.filter((item) => item.id !== id));
      }, 6000);
      toastTimersRef.current.set(id, timerId);
    },
    []
  );

  const dismissToast = useCallback((id: string) => {
    const timerId = toastTimersRef.current.get(id);
    if (timerId) {
      clearTimeout(timerId);
      toastTimersRef.current.delete(id);
    }
    setToasts((prev) => prev.filter((item) => item.id !== id));
  }, []);

  useEffect(() => {
    const timers = toastTimersRef.current;
    return () => {
      for (const t of timers.values()) {
        clearTimeout(t);
      }
      timers.clear();
      if (progressClearTimerRef.current) {
        clearTimeout(progressClearTimerRef.current);
      }
    };
  }, []);

  useEffect(() => {
    document.title = VIEW_TITLES[activeView] ?? 'WinThumbsPreloader';
  }, [activeView]);

  useEffect(() => {
    let active = true;

    Promise.allSettled([
      bridge.getBootstrap(),
      bridge.cache.getState(),
      bridge.codecs.getState(),
      bridge.optimizer.getState(),
      bridge.schedule.getState(),
    ]).then(([bootRes, cacheRes, codecsRes, optimizerRes, scheduleRes]) => {
      if (!active) return;
      if (bootRes.status === 'fulfilled') {
        const b = bootRes.value;
        setBootstrap(b);
        setActiveView(b.initialView || 'overview');
        syncBootstrapTheme(b.theme);
      }
      if (cacheRes.status === 'fulfilled') {
        setCacheSnapshot(cacheRes.value);
      }
      if (codecsRes.status === 'fulfilled') {
        setCodecsState(codecsRes.value);
      }
      if (optimizerRes.status === 'fulfilled') {
        setOptimizerState(optimizerRes.value);
      }
      if (scheduleRes.status === 'fulfilled') {
        setScheduleState(scheduleRes.value);
      }
    });

    bridge
      .checkForUpdates()
      .then((status) => {
        if (!active) return;
        setUpdateStatus(status);
      })
      .catch(() => {
        // Silent background update check failure
      });

    return () => {
      active = false;
    };
  }, [syncBootstrapTheme]);

  useEffect(() => {
    const unsubs = [
      subscribe<{ view: ViewId }>('shell:navigate', (payload) => {
        if (payload?.view) {
          setActiveView(payload.view);
        }
      }),
      subscribe<CacheSnapshot>('cache:telemetry', (snap) => {
        if (snap) {
          setCacheSnapshot(snap);
        }
      }),
      subscribe<CacheProgressEvent>('cache:progress', (ev) => {
        if (!ev) return;
        if (progressClearTimerRef.current) {
          clearTimeout(progressClearTimerRef.current);
          progressClearTimerRef.current = null;
        }
        if (ev.done || ev.percent >= 100) {
          setCacheProgress(ev.done && ev.percent >= 100 ? null : ev);
          if (!ev.done) {
            progressClearTimerRef.current = setTimeout(() => {
              setCacheProgress(null);
            }, 600);
          }
          return;
        }
        setCacheProgress(ev);
      }),
      subscribe<CacheResetEvent>('cache:resetDetected', (ev) => {
        if (!ev) return;
        notify(ev.autoRestored && ev.restoreSucceeded ? 'info' : 'warning', ev.message);
        void bridge.cache
          .getState()
          .then((snap) => setCacheSnapshot(snap))
          .catch(() => {});
      }),
      subscribe<UpdateProgressEvent>('update:progress', (ev) => {
        if (ev) setUpdateProgress(ev);
      }),
      subscribe<UpdateStatusDto>('update:available', (status) => {
        if (status) setUpdateStatus(status);
      }),
    ];

    return () => {
      for (const unsub of unsubs) {
        unsub();
      }
      if (progressClearTimerRef.current) {
        clearTimeout(progressClearTimerRef.current);
      }
    };
  }, [notify]);

  const activeCodecsCount = (codecsState?.families ?? []).filter(
    (f) => f.isEnabled || f.isPartiallyEnabled
  ).length;
  const totalCodecsCount = codecsState?.families?.length ?? 0;

  const optimizerTweaks = optimizerState?.tweaks ?? [];
  const appliedOptimizationsCount =
    optimizerState !== null
      ? optimizerTweaks.filter((t) => t.enabled).length
      : null;
  const totalOptimizationsCount =
    optimizerState !== null ? optimizerTweaks.length : null;
  const scheduleEnabled =
    scheduleState !== null
      ? Boolean(scheduleState.taskExists && scheduleState.enabled)
      : null;

  const mergePreloadItems = useCallback(
    (items: PreloadPathValidationItem[]) => {
      setPreloadQueue((prev) => {
        const map = new Map<string, PreloadPathValidationItem>();
        for (const existing of prev) {
          const key = (existing.normalizedPath || existing.input).toLowerCase();
          map.set(key, existing);
        }
        for (const next of items) {
          const key = (next.normalizedPath || next.input).toLowerCase();
          map.set(key, next);
        }
        return Array.from(map.values());
      });
    },
    []
  );

  return (
    <>
      <LiveAnnouncer
        politeMessage={politeMessage}
        assertiveMessage={assertiveMessage}
      />

      <Shell
        activeView={activeView}
        onNavigate={setActiveView}
        appName={bootstrap?.appName ?? 'WinThumbsPreloader'}
        version={bootstrap?.version ?? '3.4.0'}
        isAdmin={bootstrap?.isRunningAsAdmin ?? false}
        effectiveTheme={effectiveTheme}
        onToggleTheme={() =>
          void setTheme(effectiveTheme === 'dark' ? 'light' : 'dark')
        }
        cacheSnapshot={cacheSnapshot}
        cacheProgress={cacheProgress}
        stagedQueueCount={preloadQueue.length}
        activeCodecsCount={activeCodecsCount}
        totalCodecsCount={totalCodecsCount}
        scheduleEnabled={scheduleEnabled}
        appliedOptimizationsCount={appliedOptimizationsCount}
        totalOptimizationsCount={totalOptimizationsCount}
        updateStatus={updateStatus}
        onOpenUpdateModal={() => setUpdateModalOpen(true)}
        onMinimizeToTray={() => void bridge.minimizeToTray()}
        onQuit={() => void bridge.quit()}
      >
        {activeView === 'overview' && (
          <OverviewView
            cacheSnapshot={cacheSnapshot}
            cacheProgress={cacheProgress}
            codecsState={codecsState}
            optimizerState={optimizerState}
            scheduleState={scheduleState}
            preloadQueueCount={preloadQueue.length}
            onStagePreloadItems={mergePreloadItems}
            onCacheSnapshotUpdated={setCacheSnapshot}
            onNavigate={setActiveView}
            onNotify={notify}
          />
        )}
        {activeView === 'preload' && (
          <PreloadView
            initialLaunchWarning={
              launchWarningDismissed
                ? null
                : (bootstrap?.invalidLaunchPathWarning ?? null)
            }
            onDismissLaunchWarning={() => setLaunchWarningDismissed(true)}
            queue={preloadQueue}
            onQueueChange={setPreloadQueue}
            onNotify={notify}
          />
        )}
        {activeView === 'cache' && (
          <CacheView
            snapshot={cacheSnapshot}
            progress={cacheProgress}
            onSnapshotUpdated={setCacheSnapshot}
            onClearProgress={() => setCacheProgress(null)}
            onNotify={notify}
          />
        )}
        {activeView === 'schedule' && (
          <ScheduleView
            onScheduleUpdated={setScheduleState}
            onNotify={notify}
          />
        )}
        {activeView === 'codecs' && (
          <CodecsView
            onNavigate={setActiveView}
            onCodecsUpdated={setCodecsState}
            onNotify={notify}
          />
        )}
        {activeView === 'optimizer' && (
          <OptimizeView
            onOptimizerUpdated={setOptimizerState}
            onNotify={notify}
          />
        )}
        {activeView === 'settings' && (
          <SettingsView
            themeMode={themeMode}
            systemTheme={systemTheme}
            onThemeChange={(mode) => void setTheme(mode)}
            updateStatus={updateStatus}
            onUpdateStatusChanged={setUpdateStatus}
            onOpenUpdateModal={() => setUpdateModalOpen(true)}
            onNavigate={setActiveView}
            onNotify={notify}
          />
        )}
      </Shell>

      <ToastRegion toasts={toasts} onDismiss={dismissToast} />

      <UpdateModal
        open={updateModalOpen}
        currentVersion={bootstrap?.version ?? '3.4.0'}
        updateStatus={updateStatus}
        progress={updateProgress}
        onStatusUpdated={setUpdateStatus}
        onClose={() => setUpdateModalOpen(false)}
      />
    </>
  );
}
export default App;
