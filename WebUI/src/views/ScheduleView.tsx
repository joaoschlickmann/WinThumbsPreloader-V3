import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import {
  CalendarClock,
  ExternalLink,
  FolderPlus,
  HardDrive,
  RotateCcw,
  Save,
  Trash2,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import type { DriveSortChoice, ScheduleState } from '../lib/types';
import { Button } from '../components/ui/Button';
import { DraftNumberInput } from '../components/ui/DraftNumberInput';
import { GlassPanel } from '../components/ui/GlassPanel';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { StatusPill } from '../components/ui/StatusPill';
import { Switch } from '../components/ui/Switch';

export interface ScheduleViewProps {
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

type ActiveFrequency = 'Hour' | 'Day' | 'Week' | 'Month';

const HOURS_24 = Array.from({ length: 24 }, (_, i) => i);
const HOURS_12 = [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
const MINUTES_60 = Array.from({ length: 60 }, (_, i) => i);

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

function to12HourParts(hour24: number): { hour12: number; period: 'AM' | 'PM' } {
  const period: 'AM' | 'PM' = hour24 >= 12 ? 'PM' : 'AM';
  const mod = hour24 % 12;
  return { hour12: mod === 0 ? 12 : mod, period };
}

function to24Hour(hour12: number, period: 'AM' | 'PM'): number {
  const normalized = hour12 % 12;
  return period === 'PM' ? normalized + 12 : normalized;
}

export function ScheduleView({ onNotify }: ScheduleViewProps) {
  const [state, setState] = useState<ScheduleState | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const [enabled, setEnabled] = useState(true);
  const [frequency, setFrequency] = useState<ActiveFrequency>('Day');
  const [hour, setHour] = useState(3);
  const [minute, setMinute] = useState(0);
  const [onIdle, setOnIdle] = useState(false);
  const [multithreaded, setMultithreaded] = useState(true);
  const [threads, setThreads] = useState(0);
  const [selectedDrives, setSelectedDrives] = useState<string[]>([]);
  const [customPathsText, setCustomPathsText] = useState('');

  const [driveSelectionEnabled, setDriveSelectionEnabled] = useState(true);
  const [driveSortChoice, setDriveSortChoice] =
    useState<DriveSortChoice>('Vertical');
  const [twentyFourHourMode, setTwentyFourHourMode] = useState(false);
  const [detailedTimeMode, setDetailedTimeMode] = useState(false);

  const hourSelectId = useId();
  const minuteSelectId = useId();
  const periodSelectId = useId();
  const threadsId = useId();
  const customPathsId = useId();

  const applyServerState = useCallback((next: ScheduleState) => {
    setState(next);
    setEnabled(next.taskExists ? next.enabled : true);
    setFrequency(
      next.frequency === 'Hour' ||
        next.frequency === 'Day' ||
        next.frequency === 'Week' ||
        next.frequency === 'Month'
        ? next.frequency
        : 'Day'
    );
    setHour(next.hour);
    setMinute(next.minute);
    setOnIdle(next.onIdle);
    setMultithreaded(next.multithreaded);
    setThreads(next.threads);
    setSelectedDrives(next.drives);
    setCustomPathsText(next.customPaths.join('\n'));
    setDriveSelectionEnabled(next.driveSelectionEnabled);
    setDriveSortChoice(next.driveSortChoice);
    setTwentyFourHourMode(next.twentyFourHourMode);
    setDetailedTimeMode(next.detailedTimeMode);
  }, []);

  useEffect(() => {
    let active = true;
    bridge.schedule
      .getState()
      .then((res) => {
        if (!active) return;
        applyServerState(res);
      })
      .catch((err) => {
        if (!active) return;
        onNotify(
          'error',
          err instanceof Error ? err.message : 'Failed to load scheduled task.'
        );
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [applyServerState, onNotify]);

  const sortedDrives = useMemo(() => {
    const drives = state?.availableDrives ?? [];
    return [...drives].sort((a, b) => a.letter.localeCompare(b.letter));
  }, [state?.availableDrives]);

  const parseCustomPaths = useCallback((rawText: string): string[] => {
    return rawText
      .split(/[\r\n]+/)
      .map((line) => line.trim())
      .filter(Boolean);
  }, []);

  const persistPreferences = async (next: {
    driveSelectionEnabled: boolean;
    driveSortChoice: DriveSortChoice;
    twentyFourHourMode: boolean;
    detailedTimeMode: boolean;
  }) => {
    try {
      const res = await bridge.schedule.savePreferences(next);
      if (res.state) {
        setState((prev) =>
          prev
            ? {
                ...prev,
                driveSelectionEnabled: res.state!.driveSelectionEnabled,
                driveSortChoice: res.state!.driveSortChoice,
                twentyFourHourMode: res.state!.twentyFourHourMode,
                detailedTimeMode: res.state!.detailedTimeMode,
                availableDrives: res.state!.availableDrives,
              }
            : res.state
        );
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to save schedule display preferences.'
      );
    }
  };

  const handleToggleDriveSelection = (nextEnabled: boolean) => {
    setDriveSelectionEnabled(nextEnabled);
    const lines = parseCustomPaths(customPathsText);

    if (!nextEnabled) {
      const merged = [...lines];
      for (const letter of selectedDrives) {
        const root = `${letter.toUpperCase()}:\\`;
        if (!merged.some((p) => p.toLowerCase() === root.toLowerCase())) {
          merged.push(root);
        }
      }
      setSelectedDrives([]);
      setCustomPathsText(merged.join('\n'));
    } else {
      const extractedDrives = [...selectedDrives];
      const remainingLines: string[] = [];
      for (const line of lines) {
        if (/^[a-zA-Z]:\\$/.test(line)) {
          const letter = line[0].toUpperCase();
          if (!extractedDrives.includes(letter)) {
            extractedDrives.push(letter);
          }
        } else {
          remainingLines.push(line);
        }
      }
      setSelectedDrives(extractedDrives);
      setCustomPathsText(remainingLines.join('\n'));
    }

    void persistPreferences({
      driveSelectionEnabled: nextEnabled,
      driveSortChoice,
      twentyFourHourMode,
      detailedTimeMode,
    });
  };

  const handleToggleEnabled = async (nextEnabled: boolean) => {
    setEnabled(nextEnabled);
    if (!state?.taskExists) return;
    try {
      const res = await bridge.schedule.setEnabled(nextEnabled);
      if (res.state) {
        applyServerState(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to update task state.'
      );
    }
  };

  const handleToggleDrive = (letter: string) => {
    setSelectedDrives((prev) =>
      prev.includes(letter)
        ? prev.filter((d) => d !== letter)
        : [...prev, letter].sort()
    );
  };

  const handleAddCustomFolder = async () => {
    try {
      const res = await bridge.preload.pickFolder(true);
      if (!res.cancelled && res.paths.length > 0) {
        const current = parseCustomPaths(customPathsText);
        const merged = [...current];
        for (const p of res.paths) {
          if (!merged.some((x) => x.toLowerCase() === p.toLowerCase())) {
            merged.push(p);
          }
        }
        setCustomPathsText(merged.join('\n'));
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to browse folder.'
      );
    }
  };

  const handleSaveTask = async () => {
    setSaving(true);
    try {
      const customPaths = parseCustomPaths(customPathsText);
      const effectiveMinute = detailedTimeMode ? minute : 0;
      const res = await bridge.schedule.saveTask({
        frequency,
        hour,
        minute: effectiveMinute,
        onIdle,
        enabled,
        multithreaded,
        threads: Math.max(0, Math.min(256, Number(threads) || 0)),
        drives: driveSelectionEnabled ? selectedDrives : [],
        customPaths,
      });
      if (res.state) {
        applyServerState(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to save scheduled task.'
      );
    } finally {
      setSaving(false);
    }
  };

  const handleDeleteTask = async () => {
    setConfirmDeleteOpen(false);
    setSaving(true);
    try {
      const res = await bridge.schedule.deleteTask();
      if (res.state) {
        applyServerState(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to delete scheduled task.'
      );
    } finally {
      setSaving(false);
    }
  };

  const handleResetDisplayDefaults = async () => {
    try {
      const res = await bridge.schedule.resetPreferences();
      if (res.state) {
        setDriveSelectionEnabled(res.state.driveSelectionEnabled);
        setDriveSortChoice(res.state.driveSortChoice);
        setTwentyFourHourMode(res.state.twentyFourHourMode);
        setDetailedTimeMode(res.state.detailedTimeMode);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to reset display defaults.'
      );
    }
  };

  const handleOpenTaskScheduler = async () => {
    try {
      const res = await bridge.schedule.openTaskScheduler();
      if (!res.ok) {
        onNotify('error', res.message);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to open Windows Task Scheduler.'
      );
    }
  };

  const { hour12, period } = to12HourParts(hour);

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Scheduled Preloading
          </h1>
          <p className="ds-subtitle">
            Automate background thumbnail preloading through Windows Task
            Scheduler (<span className="ds-mono">WinThumbsPreloaderTask</span>).
          </p>
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => void handleOpenTaskScheduler()}
        >
          <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
          <span>Open Windows Task Scheduler</span>
        </Button>
      </div>

      <GlassPanel
        title="Task Status"
        actions={
          state?.taskExists ? (
            <StatusPill tone={state.enabled ? 'active' : 'warning'}>
              {state.enabled ? 'Enabled' : 'Disabled'}
            </StatusPill>
          ) : (
            <StatusPill tone="neutral">Not Scheduled</StatusPill>
          )
        }
      >
        <div className="ds-grid-2">
          <div className="ds-card ds-stack-sm">
            <div className="ds-row">
              <CalendarClock size={16} strokeWidth={2} aria-hidden="true" />
              <span className="ds-label">Last &amp; Next Execution</span>
            </div>
            <p className="ds-hint" style={{ margin: 0 }}>
              {loading
                ? 'Querying Windows Task Scheduler...'
                : state?.lastRunTimeText || 'The task has not been scheduled yet.'}
            </p>
            {state?.nextRunTimeText && (
              <p className="ds-hint" style={{ margin: 0 }}>
                {state.nextRunTimeText}
              </p>
            )}
          </div>

          <div className="ds-card">
            <Switch
              label="Task Enabled"
              hint={
                state?.taskExists
                  ? 'Toggle immediately enables or disables the Windows scheduled task.'
                  : 'Save a scheduled task below before toggling its live active state.'
              }
              checked={enabled}
              disabled={loading}
              onChange={(checked) => void handleToggleEnabled(checked)}
            />
          </div>
        </div>
      </GlassPanel>

      <GlassPanel title="Trigger &amp; Timing">
        <div className="ds-grid-2">
          <div className="ds-field">
            <span className="ds-label">Frequency</span>
            <SegmentedControl
              ariaLabel="Schedule frequency"
              value={frequency}
              onChange={setFrequency}
              options={[
                { value: 'Hour', label: 'Hour' },
                { value: 'Day', label: 'Day' },
                { value: 'Week', label: 'Week' },
                { value: 'Month', label: 'Month' },
              ]}
            />
          </div>

          <div className="ds-field">
            <span className="ds-label">Start Time</span>
            <div className="ds-row">
              {twentyFourHourMode ? (
                <select
                  id={hourSelectId}
                  aria-label="Hour (24-hour)"
                  className="ds-select"
                  style={{ width: 'auto', minWidth: 110 }}
                  value={hour}
                  onChange={(e) => setHour(Number(e.target.value))}
                >
                  {HOURS_24.map((h) => (
                    <option key={h} value={h}>
                      {detailedTimeMode ? pad2(h) : `${pad2(h)}:00`}
                    </option>
                  ))}
                </select>
              ) : (
                <select
                  id={hourSelectId}
                  aria-label="Hour (12-hour)"
                  className="ds-select"
                  style={{ width: 'auto', minWidth: 110 }}
                  value={hour12}
                  onChange={(e) =>
                    setHour(to24Hour(Number(e.target.value), period))
                  }
                >
                  {HOURS_12.map((h) => (
                    <option key={h} value={h}>
                      {detailedTimeMode ? String(h) : `${h}:00`}
                    </option>
                  ))}
                </select>
              )}

              {detailedTimeMode && (
                <select
                  id={minuteSelectId}
                  aria-label="Minute"
                  className="ds-select"
                  style={{ width: 'auto', minWidth: 96 }}
                  value={minute}
                  onChange={(e) => setMinute(Number(e.target.value))}
                >
                  {MINUTES_60.map((m) => (
                    <option key={m} value={m}>
                      :{pad2(m)}
                    </option>
                  ))}
                </select>
              )}

              {!twentyFourHourMode && (
                <select
                  id={periodSelectId}
                  aria-label="AM or PM"
                  className="ds-select"
                  style={{ width: 'auto', minWidth: 88 }}
                  value={period}
                  onChange={(e) =>
                    setHour(
                      to24Hour(hour12, e.target.value === 'PM' ? 'PM' : 'AM')
                    )
                  }
                >
                  <option value="AM">AM</option>
                  <option value="PM">PM</option>
                </select>
              )}
            </div>
          </div>
        </div>

        <hr className="ds-divider" />

        <Switch
          label="Run When Idle"
          hint="Add an idle trigger so preloading also starts when the computer is idle."
          checked={onIdle}
          onChange={setOnIdle}
        />

        <hr className="ds-divider" />

        <div className="ds-grid-2">
          <Switch
            label="Multi-Threaded"
            hint="Pass -m to preload directories using multiple worker threads."
            checked={multithreaded}
            onChange={setMultithreaded}
          />
          <div className="ds-field">
            <label htmlFor={threadsId} className="ds-label">
              Threads (0 = Auto, 0-256)
            </label>
            <div className="ds-row">
              <DraftNumberInput
                id={threadsId}
                min={0}
                max={256}
                fallback={0}
                className="ds-input"
                style={{ flex: 1 }}
                value={threads}
                disabled={!multithreaded}
                onCommit={setThreads}
              />
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setMultithreaded(true);
                  setThreads(0);
                }}
              >
                Auto
              </Button>
            </div>
          </div>
        </div>
      </GlassPanel>

      <GlassPanel
        title="Target Drives &amp; Custom Paths"
        actions={
          <Button
            variant="ghost"
            size="sm"
            onClick={() => void handleResetDisplayDefaults()}
          >
            <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Reset Display Defaults</span>
          </Button>
        }
      >
        <div className="ds-card ds-stack-sm">
          <span className="ds-label">Display Preferences</span>
          <div className="ds-grid-2">
            <Switch
              label="Enable Drive Selection"
              hint="Show the A-Z drive grid. Disabling moves selected drives into Custom Paths."
              checked={driveSelectionEnabled}
              onChange={handleToggleDriveSelection}
            />
            <Switch
              label="24-Hour Time"
              hint="Display start hours from 00:00 to 23:00 instead of 12-hour AM/PM."
              checked={twentyFourHourMode}
              onChange={(checked) => {
                setTwentyFourHourMode(checked);
                void persistPreferences({
                  driveSelectionEnabled,
                  driveSortChoice,
                  twentyFourHourMode: checked,
                  detailedTimeMode,
                });
              }}
            />
            <Switch
              label="Detailed Time (Minutes)"
              hint="Show minute-level selection in addition to hourly steps."
              checked={detailedTimeMode}
              onChange={(checked) => {
                setDetailedTimeMode(checked);
                void persistPreferences({
                  driveSelectionEnabled,
                  driveSortChoice,
                  twentyFourHourMode,
                  detailedTimeMode: checked,
                });
              }}
            />
            <div className="ds-field" style={{ justifyContent: 'center' }}>
              <span className="ds-label">Drive Sort Order</span>
              <SegmentedControl
                ariaLabel="Drive sort order"
                value={driveSortChoice}
                onChange={(nextSort) => {
                  setDriveSortChoice(nextSort);
                  void persistPreferences({
                    driveSelectionEnabled,
                    driveSortChoice: nextSort,
                    twentyFourHourMode,
                    detailedTimeMode,
                  });
                }}
                options={[
                  { value: 'Vertical', label: 'Vertical' },
                  { value: 'Horizontal', label: 'Horizontal' },
                ]}
              />
            </div>
          </div>
        </div>

        {driveSelectionEnabled && (
          <div className="ds-stack-sm">
            <div className="ds-row-between">
              <span className="ds-label">
                Drives A-Z ({selectedDrives.length} selected)
              </span>
              {selectedDrives.length > 0 && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setSelectedDrives([])}
                >
                  Clear Drives
                </Button>
              )}
            </div>
            <div
              className={`ds-drive-grid ${
                driveSortChoice === 'Horizontal'
                  ? 'ds-drive-grid-horizontal'
                  : 'ds-drive-grid-vertical'
              }`}
            >
              {sortedDrives.map((drive) => {
                const pressed = selectedDrives.includes(drive.letter);
                const meta = drive.isMounted
                  ? drive.label
                    ? `${drive.driveType} · ${drive.label}`
                    : drive.driveType
                  : 'Unmounted';
                return (
                  <button
                    key={drive.letter}
                    type="button"
                    aria-pressed={pressed}
                    className="ds-drive-btn"
                    onClick={() => handleToggleDrive(drive.letter)}
                  >
                    <span className="ds-row" style={{ gap: 6 }}>
                      <HardDrive size={14} strokeWidth={2} aria-hidden="true" />
                      <span className="ds-drive-letter">{drive.root}</span>
                    </span>
                    <span className="ds-drive-meta ds-ellipsis">{meta}</span>
                  </button>
                );
              })}
            </div>
          </div>
        )}

        <div className="ds-field">
          <div className="ds-row-between">
            <label htmlFor={customPathsId} className="ds-label">
              Custom Paths (One per line)
            </label>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void handleAddCustomFolder()}
            >
              <FolderPlus size={14} strokeWidth={2} aria-hidden="true" />
              <span>Add Folder</span>
            </Button>
          </div>
          <textarea
            id={customPathsId}
            className="ds-textarea ds-mono"
            rows={4}
            value={customPathsText}
            onChange={(e) => setCustomPathsText(e.target.value)}
            placeholder="D:\Media\Incoming&#10;\\NAS\Photos"
          />
        </div>

        <div className="ds-row-between">
          <Button
            variant="danger"
            disabled={!state?.taskExists || saving}
            onClick={() => setConfirmDeleteOpen(true)}
          >
            <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
            <span>Delete Scheduled Task</span>
          </Button>
          <Button
            variant="primary"
            loading={saving}
            loadingLabel="Saving..."
            onClick={() => void handleSaveTask()}
          >
            <Save size={14} strokeWidth={2} aria-hidden="true" />
            <span>Save Scheduled Task</span>
          </Button>
        </div>
      </GlassPanel>

      <ConfirmDialog
        open={confirmDeleteOpen}
        title="Delete scheduled task?"
        description="This removes WinThumbsPreloaderTask from Windows Task Scheduler. You can recreate it at any time."
        confirmLabel="Delete Scheduled Task"
        destructive
        onConfirm={() => void handleDeleteTask()}
        onCancel={() => setConfirmDeleteOpen(false)}
      />
    </div>
  );
}
