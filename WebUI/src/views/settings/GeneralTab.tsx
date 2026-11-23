import { useEffect, useId, useState } from 'react';
import {
  AlertTriangle,
  Monitor,
  Moon,
  RotateCcw,
  Save,
  Shield,
  Sun,
} from 'lucide-react';
import { bridge } from '../../lib/bridge';
import type {
  GeneralSettingsDto,
  SettingsStateDto,
  ThemeMode,
} from '../../lib/types';
import { Button } from '../../components/ui/Button';
import { DraftNumberInput } from '../../components/ui/DraftNumberInput';
import { GlassPanel } from '../../components/ui/GlassPanel';
import { ConfirmDialog } from '../../components/ui/ModalDialog';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { StatusPill } from '../../components/ui/StatusPill';
import { Switch } from '../../components/ui/Switch';

const THUMBNAIL_SIZES = [
  '16',
  '32',
  '48',
  '96',
  '256',
  '768',
  '1280',
  '1920',
  '2560',
] as const;

const PROCESS_PRIORITIES = [
  'Realtime',
  'High',
  'Above Normal',
  'Normal',
  'Below Normal',
  'Idle',
] as const;

const TIME_UNITS = ['Seconds', 'Minutes', 'Hours'] as const;

export interface GeneralTabProps {
  general: GeneralSettingsDto;
  themeMode: ThemeMode;
  systemTheme: 'dark' | 'light';
  onThemeChange: (next: ThemeMode) => void;
  onStateUpdated: (next: SettingsStateDto) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

export function GeneralTab({
  general,
  themeMode,
  systemTheme,
  onThemeChange,
  onStateUpdated,
  onNotify,
}: GeneralTabProps) {
  const [admin, setAdmin] = useState(general.admin);
  const [multithreaded, setMultithreaded] = useState(general.multithreaded);
  const [threadCount, setThreadCount] = useState(general.threadCount);
  const [selectedSizes, setSelectedSizes] = useState<string[]>(() =>
    general.preloaderThumbnailSizes
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean)
  );
  const [priority, setPriority] = useState(general.preloaderProcessPriority);
  const [dialogSpeed, setDialogSpeed] = useState(
    general.progressDialogUpdateSpeed
  );
  const [waitPreload, setWaitPreload] = useState(general.waitAfterPreloading);
  const [waitPreloadTime, setWaitPreloadTime] = useState(
    general.waitTimeAfterPreloading
  );
  const [waitPreloadUnit, setWaitPreloadUnit] = useState(
    general.waitAfterPreloadingUnit
  );
  const [waitCache, setWaitCache] = useState(general.waitAfterCacheBackup);
  const [waitCacheTime, setWaitCacheTime] = useState(
    general.waitTimeAfterCacheBackup
  );
  const [waitCacheUnit, setWaitCacheUnit] = useState(general.waitAfterCacheUnit);

  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [confirmResetOpen, setConfirmResetOpen] = useState(false);
  const [confirmElevateOpen, setConfirmElevateOpen] = useState(false);
  const [relaunchRequired, setRelaunchRequired] = useState(
    general.admin !== general.isRunningAsAdmin
  );

  const threadsId = useId();
  const priorityId = useId();
  const speedId = useId();
  const presetId = useId();
  const waitPreloadValId = useId();
  const waitPreloadUnitId = useId();
  const waitCacheValId = useId();
  const waitCacheUnitId = useId();

  useEffect(() => {
    setAdmin(general.admin);
    setMultithreaded(general.multithreaded);
    setThreadCount(general.threadCount);
    setSelectedSizes(
      general.preloaderThumbnailSizes
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)
    );
    setPriority(general.preloaderProcessPriority);
    setDialogSpeed(general.progressDialogUpdateSpeed);
    setWaitPreload(general.waitAfterPreloading);
    setWaitPreloadTime(general.waitTimeAfterPreloading);
    setWaitPreloadUnit(general.waitAfterPreloadingUnit);
    setWaitCache(general.waitAfterCacheBackup);
    setWaitCacheTime(general.waitTimeAfterCacheBackup);
    setWaitCacheUnit(general.waitAfterCacheUnit);
    setRelaunchRequired(general.admin !== general.isRunningAsAdmin);
  }, [general]);

  const toggleSize = (size: string) => {
    setSelectedSizes((prev) => {
      if (prev.includes(size)) {
        if (prev.length <= 1) {
          onNotify(
            'warning',
            'At least one requested thumbnail size must remain selected.'
          );
          return prev;
        }
        return prev.filter((s) => s !== size);
      }
      return THUMBNAIL_SIZES.filter((s) => s === size || prev.includes(s));
    });
  };

  const currentPresetValue =
    selectedSizes.join(',') === '96,256'
      ? '96,256'
      : selectedSizes.join(',') === '256'
        ? '256'
        : selectedSizes.join(',') === '768'
          ? '768'
          : 'custom';

  const applyPreset = (value: string) => {
    if (value === '96,256') setSelectedSizes(['96', '256']);
    else if (value === '256') setSelectedSizes(['256']);
    else if (value === '768') setSelectedSizes(['768']);
  };

  const handleSaveGeneral = async () => {
    setBusyAction('save');
    try {
      const preloadMax = waitPreloadUnit === 'Hours' ? 24 : 60;
      const cacheMax = waitCacheUnit === 'Hours' ? 24 : 60;
      const res = await bridge.settings.saveGeneral({
        admin,
        multithreaded,
        threadCount: Math.max(0, Math.min(512, threadCount)),
        preloaderThumbnailSizes: selectedSizes.join(','),
        preloaderProcessPriority: priority,
        progressDialogUpdateSpeed: Math.max(100, Math.min(1000, dialogSpeed)),
        waitAfterPreloading: waitPreload,
        waitTimeAfterPreloading: Math.max(
          0,
          Math.min(preloadMax, waitPreloadTime)
        ),
        waitAfterPreloadingUnit: waitPreloadUnit,
        waitAfterCacheBackup: waitCache,
        waitTimeAfterCacheBackup: Math.max(0, Math.min(cacheMax, waitCacheTime)),
        waitAfterCacheUnit: waitCacheUnit,
      });
      if (res.state) {
        onStateUpdated(res.state);
      }
      setRelaunchRequired(res.relaunchRequired);
      onNotify(res.ok ? (res.relaunchRequired ? 'warning' : 'info') : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to save general settings.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleResetGeneral = async () => {
    setConfirmResetOpen(false);
    setBusyAction('reset');
    try {
      const res = await bridge.settings.resetGeneral();
      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to reset general defaults.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleRequestElevation = async () => {
    setConfirmElevateOpen(false);
    setBusyAction('elevate');
    try {
      const res = await bridge.settings.requestAdminElevation();
      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'warning', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to request administrator elevation.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const showPriorityWarning =
    priority === 'Realtime' || priority === 'High' || priority === 'Idle';

  const systemLabel =
    systemTheme === 'dark' ? 'System (Dark)' : 'System (Light)';

  return (
    <div className="ds-stack-md">
      <GlassPanel title="Appearance Theme">
        <div className="ds-row-between">
          <div className="ds-stack-sm">
            <span className="ds-resource-name">Color Scheme</span>
            <span className="ds-hint">
              Switch between Polished Obsidian (dark), Liquid Light, or follow
              Windows OS preference.
            </span>
          </div>
          <SegmentedControl
            ariaLabel="Appearance Theme"
            value={themeMode}
            onChange={onThemeChange}
            options={[
              {
                value: 'light',
                label: 'Light',
                icon: <Sun size={14} strokeWidth={2} aria-hidden="true" />,
              },
              {
                value: 'dark',
                label: 'Dark',
                icon: <Moon size={14} strokeWidth={2} aria-hidden="true" />,
              },
              {
                value: 'system',
                label: systemLabel,
                icon: <Monitor size={14} strokeWidth={2} aria-hidden="true" />,
              },
            ]}
          />
        </div>
      </GlassPanel>

      <GlassPanel
        title="Privilege &amp; Concurrency"
        actions={
          <div className="ds-row">
            <StatusPill
              tone={
                general.isRunningAsAdmin
                  ? 'active'
                  : relaunchRequired
                    ? 'warning'
                    : 'neutral'
              }
              icon={<Shield size={12} strokeWidth={2} aria-hidden="true" />}
            >
              {general.isRunningAsAdmin
                ? 'Running as Administrator'
                : relaunchRequired
                  ? 'Relaunch Required'
                  : 'Standard User'}
            </StatusPill>
            {!general.isRunningAsAdmin && (
              <Button
                variant="secondary"
                size="sm"
                loading={busyAction === 'elevate'}
                onClick={() => setConfirmElevateOpen(true)}
              >
                <Shield size={14} strokeWidth={2} aria-hidden="true" />
                <span>Request Administrator Elevation</span>
              </Button>
            )}
          </div>
        }
      >
        <Switch
          label="Run As Admin"
          hint="Launch preloader jobs elevated. Scans protected directories, though some user-scoped shell extensions (such as PowerToys) may require system-wide codec registration."
          checked={admin}
          onChange={setAdmin}
        />

        <hr className="ds-divider" />

        <Switch
          label="Multi-Threaded"
          hint="Enable parallel thumbnail extraction by default."
          checked={multithreaded}
          onChange={setMultithreaded}
        />

        <div className="ds-grid-2">
          <div className="ds-field">
            <label htmlFor={threadsId} className="ds-label">
              Threads (0 = Auto, 0-512)
            </label>
            <div className="ds-row">
              <DraftNumberInput
                id={threadsId}
                min={0}
                max={512}
                fallback={0}
                className="ds-input"
                style={{ flex: 1 }}
                disabled={!multithreaded}
                value={threadCount}
                onCommit={setThreadCount}
              />
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setMultithreaded(true);
                  setThreadCount(0);
                }}
              >
                Auto (0)
              </Button>
            </div>
          </div>

          <div className="ds-field">
            <label htmlFor={speedId} className="ds-label">
              Progress Dialog Update Speed (100-1000 ms)
            </label>
            <DraftNumberInput
              id={speedId}
              min={100}
              max={1000}
              fallback={250}
              className="ds-input"
              value={dialogSpeed}
              onCommit={setDialogSpeed}
            />
          </div>
        </div>

        <div className="ds-field">
          <label htmlFor={priorityId} className="ds-label">
            Preloader Process Priority
          </label>
          <select
            id={priorityId}
            className="ds-select"
            value={priority}
            onChange={(e) => setPriority(e.target.value)}
          >
            {PROCESS_PRIORITIES.map((p) => (
              <option key={p} value={p}>
                {p}
              </option>
            ))}
          </select>
          {showPriorityWarning && (
            <div role="status" className="ds-alert ds-alert-warning">
              <div className="ds-alert-body">
                <AlertTriangle size={16} strokeWidth={2} aria-hidden="true" />
                <span>
                  {priority === 'Idle'
                    ? 'Idle priority yields to all other background tasks and can take significantly longer on busy systems.'
                    : `${priority} priority may reduce desktop and Explorer responsiveness during large media scans.`}
                </span>
              </div>
            </div>
          )}
        </div>
      </GlassPanel>

      <GlassPanel title="Requested Thumbnail Sizes">
        <div className="ds-field">
          <label htmlFor={presetId} className="ds-label">
            Size Presets
          </label>
          <select
            id={presetId}
            className="ds-select"
            value={currentPresetValue}
            onChange={(e) => applyPreset(e.target.value)}
          >
            <option value="96,256">Most Common Sizes (96, 256)</option>
            <option value="256">Explorer Size / Photos Small (256)</option>
            <option value="768">Photos Large / Medium (768)</option>
            {currentPresetValue === 'custom' && (
              <option value="custom">Custom Selection</option>
            )}
          </select>
        </div>

        <div
          className="ds-size-grid"
          role="group"
          aria-label="Requested thumbnail sizes"
        >
          {THUMBNAIL_SIZES.map((size) => (
            <label key={size} className="ds-size-option">
              <input
                type="checkbox"
                className="ds-checkbox"
                checked={selectedSizes.includes(size)}
                onChange={() => toggleSize(size)}
              />
              <span>{size} px</span>
            </label>
          ))}
        </div>
      </GlassPanel>

      <GlassPanel title="Completion &amp; Backup Delays">
        <div className="ds-stack-md">
          <Switch
            label="Wait After Preload Completion"
            hint="Pause briefly before closing the progress dialog (0 = wait indefinitely)."
            checked={waitPreload}
            onChange={setWaitPreload}
          />
          <div className="ds-row">
            <div className="ds-field" style={{ flex: '1 1 140px' }}>
              <label htmlFor={waitPreloadValId} className="ds-label">
                Delay (0 = wait indefinitely)
              </label>
              <DraftNumberInput
                id={waitPreloadValId}
                min={0}
                max={waitPreloadUnit === 'Hours' ? 24 : 60}
                fallback={0}
                className="ds-input"
                disabled={!waitPreload}
                value={waitPreloadTime}
                onCommit={setWaitPreloadTime}
              />
            </div>
            <div className="ds-field" style={{ flex: '1 1 160px' }}>
              <label htmlFor={waitPreloadUnitId} className="ds-label">
                Unit
              </label>
              <select
                id={waitPreloadUnitId}
                className="ds-select"
                disabled={!waitPreload}
                value={waitPreloadUnit}
                onChange={(e) => setWaitPreloadUnit(e.target.value)}
              >
                {TIME_UNITS.map((u) => (
                  <option key={u} value={u}>
                    {u}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <hr className="ds-divider" />

          <Switch
            label="Wait After Cache Backup"
            hint="Delay between finishing preload and triggering post-preload cache backup (0 = wait indefinitely)."
            checked={waitCache}
            onChange={setWaitCache}
          />
          <div className="ds-row">
            <div className="ds-field" style={{ flex: '1 1 140px' }}>
              <label htmlFor={waitCacheValId} className="ds-label">
                Delay (0 = wait indefinitely)
              </label>
              <DraftNumberInput
                id={waitCacheValId}
                min={0}
                max={waitCacheUnit === 'Hours' ? 24 : 60}
                fallback={0}
                className="ds-input"
                disabled={!waitCache}
                value={waitCacheTime}
                onCommit={setWaitCacheTime}
              />
            </div>
            <div className="ds-field" style={{ flex: '1 1 160px' }}>
              <label htmlFor={waitCacheUnitId} className="ds-label">
                Unit
              </label>
              <select
                id={waitCacheUnitId}
                className="ds-select"
                disabled={!waitCache}
                value={waitCacheUnit}
                onChange={(e) => setWaitCacheUnit(e.target.value)}
              >
                {TIME_UNITS.map((u) => (
                  <option key={u} value={u}>
                    {u}
                  </option>
                ))}
              </select>
            </div>
          </div>
        </div>

        <hr className="ds-divider" />

        <div className="ds-row-between">
          <span className="ds-hint">
            Saves concurrency, thumbnail size, priority, and wait timer defaults.
          </span>
          <Button
            variant="primary"
            loading={busyAction === 'save'}
            loadingLabel="Saving..."
            disabled={busyAction !== null}
            onClick={() => void handleSaveGeneral()}
          >
            <Save size={14} strokeWidth={2} aria-hidden="true" />
            <span>Save General Settings</span>
          </Button>
        </div>
      </GlassPanel>

      <section className="ds-danger-zone" aria-label="Reset general defaults">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Reset Preferences</h2>
          <p className="ds-hint">
            Restore default threads, thumbnail sizes (96, 256), Below Normal priority, and timers.
          </p>
        </div>
        <Button
          variant="danger"
          size="sm"
          loading={busyAction === 'reset'}
          disabled={busyAction !== null}
          onClick={() => setConfirmResetOpen(true)}
        >
          <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
          <span>Reset Defaults</span>
        </Button>
      </section>

      <ConfirmDialog
        open={confirmElevateOpen}
        title="Restart WinThumbsPreloader as Administrator?"
        description="Saves 'Run As Admin' and prompts Windows UAC to relaunch the application with Administrator privileges."
        confirmLabel="Restart as Administrator"
        onConfirm={() => void handleRequestElevation()}
        onCancel={() => setConfirmElevateOpen(false)}
      />

      <ConfirmDialog
        open={confirmResetOpen}
        title="Reset General &amp; Preloader settings to defaults?"
        description="Restores default thread settings, thumbnail sizes (96, 256), Below Normal priority, and completion timers."
        confirmLabel="Reset Defaults"
        destructive
        onConfirm={() => void handleResetGeneral()}
        onCancel={() => setConfirmResetOpen(false)}
      />
    </div>
  );
}
