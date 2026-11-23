import { useEffect, useId, useState } from 'react';
import {
  AlertTriangle,
  FolderOpen,
  RotateCcw,
  Save,
  Trash2,
} from 'lucide-react';
import { bridge } from '../../lib/bridge';
import type {
  LogFolderStatusDto,
  LoggingSettingsDto,
  SettingsStateDto,
} from '../../lib/types';
import { Button } from '../../components/ui/Button';
import { DraftNumberInput } from '../../components/ui/DraftNumberInput';
import { GlassPanel } from '../../components/ui/GlassPanel';
import { ConfirmDialog } from '../../components/ui/ModalDialog';
import { StatusPill, type StatusTone } from '../../components/ui/StatusPill';
import { Switch } from '../../components/ui/Switch';

export interface LoggingTabProps {
  logging: LoggingSettingsDto;
  onStateUpdated: (next: SettingsStateDto) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

const LOG_LEVELS = [
  { value: 0, label: '0: No Logging' },
  { value: 1, label: '1: Preloader Logging' },
  { value: 2, label: '2: GUI Logging' },
  { value: 3, label: '3: All Logging' },
  { value: 4, label: '4: Debug Logging' },
] as const;

function getFolderStatusTone(status: string): StatusTone {
  switch (status) {
    case 'Writable':
    case 'WillBeCreated':
      return 'active';
    case 'WritableAdmin':
    case 'WillBeCreatedAdmin':
      return 'warning';
    case 'NotWritable':
    case 'InvalidPath':
      return 'error';
    default:
      return 'neutral';
  }
}

export function LoggingTab({
  logging,
  onStateUpdated,
  onNotify,
}: LoggingTabProps) {
  const [frequency, setFrequency] = useState(logging.loggingFrequency);
  const [folderPath, setFolderPath] = useState(logging.loggerFolderPath);
  const [folderStatus, setFolderStatus] = useState<LogFolderStatusDto>(
    logging.logFolderStatus
  );
  const [logsSizeFormatted, setLogsSizeFormatted] = useState(
    logging.logsSizeFormatted
  );
  const [autoDelete, setAutoDelete] = useState(logging.autoDeleteLogsByAge);
  const [retentionDays, setRetentionDays] = useState(
    Math.min(3650, Math.max(1, logging.logRetentionDays))
  );
  const [maxSizeMb, setMaxSizeMb] = useState(logging.maxLogFileSizeMB);

  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);
  const [pendingAdminConfirmMsg, setPendingAdminConfirmMsg] = useState<
    string | null
  >(null);

  const freqId = useId();
  const folderId = useId();
  const retentionId = useId();
  const maxSizeId = useId();

  useEffect(() => {
    setFrequency(logging.loggingFrequency);
    setFolderPath(logging.loggerFolderPath);
    setFolderStatus(logging.logFolderStatus);
    setLogsSizeFormatted(logging.logsSizeFormatted);
    setAutoDelete(logging.autoDeleteLogsByAge);
    setRetentionDays(Math.min(3650, Math.max(1, logging.logRetentionDays)));
    setMaxSizeMb(logging.maxLogFileSizeMB);
  }, [logging]);

  const checkFolderStatus = async (candidatePath: string) => {
    try {
      const next = await bridge.settings.getLogFolderStatus(candidatePath);
      setFolderStatus(next);
    } catch {
      // Non-fatal status probe failure
    }
  };

  const handleBrowse = async () => {
    try {
      const res = await bridge.settings.pickLoggerFolder();
      if (!res.cancelled && res.path) {
        setFolderPath(res.path);
        if (res.logFolderStatus) {
          setFolderStatus(res.logFolderStatus);
        } else {
          await checkFolderStatus(res.path);
        }
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to select log folder.'
      );
    }
  };

  const handleSave = async (confirmAdminWarning = false) => {
    setBusyAction('save');
    try {
      const res = await bridge.settings.saveLogging({
        loggingFrequency: frequency,
        loggerFolderPath: folderPath.trim() || null,
        autoDeleteLogsByAge: autoDelete,
        logRetentionDays: Math.max(1, Math.min(3650, retentionDays)),
        maxLogFileSizeMB: Math.max(1, Math.min(10240, maxSizeMb)),
        confirmAdminWarning,
      });

      if (res.requiresConfirmation) {
        setPendingAdminConfirmMsg(res.message);
        return;
      }

      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to save logging settings.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleReset = async () => {
    setBusyAction('reset');
    try {
      const res = await bridge.settings.resetLogging();
      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to reset logging settings.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleOpenFolder = async () => {
    try {
      const res = await bridge.settings.openLogsFolder();
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to open logs folder.'
      );
    }
  };

  const handleClearLogs = async () => {
    setConfirmClearOpen(false);
    setBusyAction('clear');
    try {
      const res = await bridge.settings.clearLogs();
      setLogsSizeFormatted(res.logsSizeFormatted);
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to clear logs.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  return (
    <div className="ds-stack-md">
      <GlassPanel
        title="Diagnostic Logging"
        actions={
          <StatusPill tone="neutral">Logs Size: {logsSizeFormatted}</StatusPill>
        }
      >
        <div className="ds-field">
          <label htmlFor={freqId} className="ds-label">
            Logging Frequency
          </label>
          <select
            id={freqId}
            className="ds-select"
            value={frequency}
            onChange={(e) => setFrequency(Number(e.target.value))}
          >
            {LOG_LEVELS.map((lvl) => (
              <option key={lvl.value} value={lvl.value}>
                {lvl.label}
              </option>
            ))}
          </select>
        </div>

        {frequency === 4 && (
          <div role="alert" className="ds-alert ds-alert-warning">
            <div className="ds-alert-body">
              <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" />
              <span>
                <strong>Debug Logging Enabled:</strong> Logs almost every action
                and variable, which can slow preloading and increase disk usage.
              </span>
            </div>
          </div>
        )}

        <div className="ds-field">
          <div className="ds-row-between">
            <label htmlFor={folderId} className="ds-label">
              Logs Folder
            </label>
            <StatusPill tone={getFolderStatusTone(folderStatus.status)}>
              {folderStatus.message || folderStatus.status}
            </StatusPill>
          </div>
          <div className="ds-row">
            <input
              id={folderId}
              type="text"
              className="ds-input ds-mono"
              style={{ flex: '1 1 280px' }}
              value={folderPath}
              onChange={(e) => {
                const val = e.target.value;
                setFolderPath(val);
                void checkFolderStatus(val);
              }}
              placeholder="Choose a writable folder for log files..."
            />
            <Button variant="secondary" onClick={() => void handleBrowse()}>
              Browse
            </Button>
            <Button
              variant="secondary"
              onClick={() => {
                setFolderPath(logging.recommendedDefaultLogPath);
                void checkFolderStatus(logging.recommendedDefaultLogPath);
              }}
            >
              Use Recommended Default
            </Button>
            <Button
              variant="ghost"
              onClick={() => {
                setFolderPath('');
                void checkFolderStatus('');
              }}
            >
              Clear Path
            </Button>
          </div>
        </div>

        <hr className="ds-divider" />

        <Switch
          label="Auto-Delete Logs After"
          hint="Automatically purge log files older than the retention window."
          checked={autoDelete}
          onChange={setAutoDelete}
        />

        <div className="ds-grid-2">
          <div className="ds-field">
            <label htmlFor={retentionId} className="ds-label">
              Retention Days (1-3650)
            </label>
            <DraftNumberInput
              id={retentionId}
              min={1}
              max={3650}
              fallback={30}
              className="ds-input"
              disabled={!autoDelete}
              value={retentionDays}
              onCommit={setRetentionDays}
            />
          </div>

          <div className="ds-field">
            <label htmlFor={maxSizeId} className="ds-label">
              Max Log File Size (MB, 1-10240)
            </label>
            <DraftNumberInput
              id={maxSizeId}
              min={1}
              max={10240}
              fallback={1024}
              className="ds-input"
              value={maxSizeMb}
              onCommit={setMaxSizeMb}
            />
          </div>
        </div>

        <hr className="ds-divider" />

        <div className="ds-row-between">
          <div className="ds-row">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void handleOpenFolder()}
            >
              <FolderOpen size={14} strokeWidth={2} aria-hidden="true" />
              <span>Open Logs Folder</span>
            </Button>
            <Button
              variant="ghost"
              size="sm"
              loading={busyAction === 'reset'}
              disabled={busyAction !== null}
              onClick={() => void handleReset()}
            >
              <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
              <span>Reset Defaults</span>
            </Button>
          </div>

          <Button
            variant="primary"
            loading={busyAction === 'save'}
            loadingLabel="Saving..."
            disabled={busyAction !== null}
            onClick={() => void handleSave(false)}
          >
            <Save size={14} strokeWidth={2} aria-hidden="true" />
            <span>Save Logging</span>
          </Button>
        </div>
      </GlassPanel>

      <section className="ds-danger-zone" aria-label="Log maintenance">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Log Maintenance</h2>
          <p className="ds-hint">
            Permanently deletes all diagnostic log files stored in the configured log directory.
          </p>
        </div>
        <Button
          variant="danger"
          size="sm"
          loading={busyAction === 'clear'}
          disabled={busyAction !== null}
          onClick={() => setConfirmClearOpen(true)}
        >
          <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
          <span>Clear All Logs</span>
        </Button>
      </section>

      <ConfirmDialog
        open={confirmClearOpen}
        title="Clear all log files?"
        description="Deletes all existing WinThumbsPreloader log files inside your configured log folder."
        confirmLabel="Clear All Logs"
        destructive
        onConfirm={() => void handleClearLogs()}
        onCancel={() => setConfirmClearOpen(false)}
      />

      <ConfirmDialog
        open={Boolean(pendingAdminConfirmMsg)}
        title="Administrator permissions may be required for log folder"
        description={
          pendingAdminConfirmMsg ??
          'The chosen log folder requires elevation. Use this folder anyway?'
        }
        confirmLabel="Use Folder Anyway"
        destructive
        onConfirm={() => {
          setPendingAdminConfirmMsg(null);
          void handleSave(true);
        }}
        onCancel={() => setPendingAdminConfirmMsg(null)}
      />
    </div>
  );
}
