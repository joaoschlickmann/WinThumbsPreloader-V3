import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  Archive,
  Database,
  FolderOpen,
  RefreshCw,
  RotateCcw,
  Save,
  Trash2,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import type {
  BackupRestoreSafetyMode,
  CacheProgressEvent,
  CacheSizeFormat,
  CacheSnapshot,
  ExplorerCloseFrequency,
  ExplorerDeletionMethod,
} from '../lib/types';
import { Button } from '../components/ui/Button';
import { DraftNumberInput } from '../components/ui/DraftNumberInput';
import { GlassPanel } from '../components/ui/GlassPanel';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { StatusPill, type StatusTone } from '../components/ui/StatusPill';
import { Switch } from '../components/ui/Switch';

export interface CacheViewProps {
  snapshot: CacheSnapshot | null;
  progress: CacheProgressEvent | null;
  onSnapshotUpdated: (next: CacheSnapshot) => void;
  onClearProgress?: () => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

function getBackupStatusTone(status: string): StatusTone {
  switch (status) {
    case 'Writable':
    case 'WillBeCreated':
      return 'ok';
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

function formatLastBackup(iso: string | null): string {
  if (!iso) return 'Never';
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return 'Never';
  return parsed.toLocaleString('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

function formatBucketSizeMb(bytes: number): string {
  if (bytes <= 0) return '0 MB';
  const mb = bytes / (1024 * 1024);
  return mb < 1 ? `${(bytes / 1024).toFixed(0)} KB` : `${mb.toFixed(0)} MB`;
}

export function CacheView({
  snapshot,
  progress,
  onSnapshotUpdated,
  onClearProgress,
  onNotify,
}: CacheViewProps) {
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [lastFeedback, setLastFeedback] = useState<string | null>(null);

  const [backupFolderInput, setBackupFolderInput] = useState('');
  const [safetyMode, setSafetyMode] =
    useState<BackupRestoreSafetyMode>('Allow Partial Copy');
  const [deletionMethod, setDeletionMethod] =
    useState<ExplorerDeletionMethod>('Manual Deletion');
  const [closeFrequency, setCloseFrequency] =
    useState<ExplorerCloseFrequency>('On Every File');
  const [sizeFormat, setSizeFormat] = useState<CacheSizeFormat>('MB');
  const [refreshIntervalMs, setRefreshIntervalMs] = useState(250);
  const [autoBackupIntervalSec, setAutoBackupIntervalSec] = useState(5);
  const [autoRestoreIntervalSec, setAutoRestoreIntervalSec] = useState(5);
  const [resetScope, setResetScope] = useState<
    'all' | 'general' | 'cache' | 'backup'
  >('all');

  const [confirmAction, setConfirmAction] = useState<
    null | 'backupOverwrite' | 'restore' | 'forceRestore' | 'clearBackup' | 'clearCache' | 'resetDefaults'
  >(null);
  const [pendingAdminFolder, setPendingAdminFolder] = useState<{
    path: string;
    message: string;
  } | null>(null);

  const initializedSnapshotRef = useRef(false);
  const backupPathDirtyRef = useRef(false);
  const policyDirtyRef = useRef(false);

  const backupFolderId = useId();
  const safetyId = useId();
  const deletionId = useId();
  const closeFreqId = useId();
  const formatId = useId();
  const refreshId = useId();
  const autoBackupIntId = useId();
  const autoRestoreIntId = useId();
  const resetScopeId = useId();

  useEffect(() => {
    if (!snapshot) return;
    if (!initializedSnapshotRef.current || !backupPathDirtyRef.current) {
      setBackupFolderInput(snapshot.backupFolderPath);
    }
    if (!initializedSnapshotRef.current || !policyDirtyRef.current) {
      setSafetyMode(snapshot.backupRestoreClearSafety);
      setDeletionMethod(snapshot.explorerCacheDeletionMethod);
      setCloseFrequency(snapshot.explorerCloseFrequency);
      setSizeFormat(snapshot.cacheSizeFormat);
      setRefreshIntervalMs(snapshot.cacheUpdateIntervalMs);
      setAutoBackupIntervalSec(
        Math.max(1, Math.round(snapshot.autoBackupIntervalMs / 1000))
      );
      setAutoRestoreIntervalSec(
        Math.max(1, Math.round(snapshot.autoRestoreIntervalMs / 1000))
      );
    }
    initializedSnapshotRef.current = true;
  }, [snapshot]);

  const refreshState = useCallback(async () => {
    try {
      const next = await bridge.cache.getState();
      onSnapshotUpdated(next);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to refresh cache telemetry.'
      );
    }
  }, [onNotify, onSnapshotUpdated]);

  useEffect(() => {
    void refreshState();
  }, [refreshState]);

  const runCacheOp = async (
    actionKey: string,
    fn: () => Promise<{ ok: boolean; message: string; snapshot: CacheSnapshot | null }>
  ) => {
    setBusyAction(actionKey);
    try {
      const res = await fn();
      if (res.snapshot) {
        onSnapshotUpdated(res.snapshot);
      }
      setLastFeedback(res.message);
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Cache operation failed.';
      setLastFeedback(msg);
      onNotify('error', msg);
    } finally {
      setBusyAction(null);
      if (actionKey === 'backup' || actionKey === 'restore' || actionKey === 'forceRestore') {
        onClearProgress?.();
      }
    }
  };

  const handleSetBackupFolder = async (
    targetPath: string | null,
    confirmAdminWarning = false
  ) => {
    setBusyAction('backupPath');
    try {
      const res = await bridge.cache.setBackupFolder(
        targetPath,
        confirmAdminWarning
      );
      if (res.requiresConfirmation) {
        setPendingAdminFolder({
          path: targetPath ?? '',
          message: res.message,
        });
        return;
      }
      if (res.ok) {
        backupPathDirtyRef.current = false;
      }
      setLastFeedback(res.message);
      onNotify(res.ok ? 'info' : 'error', res.message);
      await refreshState();
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to update backup folder.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handlePickBackupFolder = async () => {
    try {
      const res = await bridge.cache.pickBackupFolder();
      if (!res.cancelled && res.path) {
        backupPathDirtyRef.current = false;
        setBackupFolderInput(res.path);
        await handleSetBackupFolder(res.path, false);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to pick backup folder.'
      );
    }
  };

  const handleSavePolicy = async () => {
    policyDirtyRef.current = false;
    await runCacheOp('savePolicy', () =>
      bridge.cache.saveAdvanced({
        autoBackupAfterPreload: snapshot?.autoBackupAfterPreload ?? true,
        backupRestoreClearSafety: safetyMode,
        explorerCacheDeletionMethod: deletionMethod,
        explorerCloseFrequency: closeFrequency,
        cacheSizeFormat: sizeFormat,
        cacheUpdateIntervalMs: Math.max(50, Math.min(60000, refreshIntervalMs)),
        autoBackupIntervalMs:
          Math.max(1, Math.min(3600, autoBackupIntervalSec)) * 1000,
        autoRestoreIntervalMs:
          Math.max(1, Math.min(3600, autoRestoreIntervalSec)) * 1000,
      })
    );
  };

  const hasBackupPath = Boolean(snapshot?.backupFolderPath?.trim());
  const saturatedBuckets = useMemo(
    () => (snapshot?.buckets ?? []).filter((b) => b.saturated),
    [snapshot]
  );

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Cache Manager
          </h1>
          <p className="ds-subtitle">
            Routine backup &amp; restore controls stay cleanly separated from
            Explorer-destructive maintenance actions.
          </p>
        </div>

        <div className="ds-primary-actions">
          <Button
            variant="primary"
            disabled={busyAction !== null || !hasBackupPath}
            loading={busyAction === 'backup'}
            loadingLabel="Backing Up..."
            onClick={() => {
              if (snapshot?.backupLargerThanExplorer) {
                setConfirmAction('backupOverwrite');
              } else {
                void runCacheOp('backup', () => bridge.cache.backup());
              }
            }}
          >
            <Database size={14} strokeWidth={2} aria-hidden="true" />
            <span>Backup Now</span>
          </Button>
          <Button
            variant="secondary"
            disabled={busyAction !== null || !hasBackupPath}
            loading={busyAction === 'restore'}
            onClick={() => setConfirmAction('restore')}
          >
            <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Restore Backup</span>
          </Button>
          <Button
            variant="secondary"
            disabled={busyAction !== null || !hasBackupPath}
            loading={busyAction === 'forceRestore'}
            onClick={() => setConfirmAction('forceRestore')}
          >
            <span>Force Restore (Restart Explorer)</span>
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => void refreshState()}
            disabled={busyAction !== null}
          >
            <RefreshCw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Refresh</span>
          </Button>
        </div>
      </div>

      {snapshot?.resetRecognized && (
        <div role="alert" className="ds-alert ds-alert-warning">
          <div className="ds-alert-body">
            <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" />
            <div>
              <strong>Explorer Thumbnail Cache Reset Detected.</strong> Your
              Windows Explorer cache is smaller than your saved backup (
              {snapshot.explorerSizeFormatted} vs{' '}
              {snapshot.backupSizeFormatted}).
            </div>
          </div>
          <div className="ds-row">
            <Button
              variant="primary"
              size="sm"
              onClick={() => setConfirmAction('restore')}
              disabled={busyAction !== null || !hasBackupPath}
            >
              Restore Backup
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setConfirmAction('forceRestore')}
              disabled={busyAction !== null || !hasBackupPath}
            >
              Force Restore
            </Button>
          </div>
        </div>
      )}

      <div className="ds-grid-2">
        <section className="ds-glass ds-card ds-stack-sm">
          <div className="ds-row-between">
            <h2 className="ds-label" style={{ margin: 0 }}>
              Explorer Cache
            </h2>
            <Button
              variant="secondary"
              size="sm"
              onClick={() =>
                void runCacheOp('openCache', () =>
                  bridge.cache.openCacheFolder()
                )
              }
            >
              <FolderOpen size={14} strokeWidth={2} aria-hidden="true" />
              <span>Open Cache Folder</span>
            </Button>
          </div>
          <div className="ds-row-between">
            <span className="ds-metric-value">
              {snapshot?.explorerSizeFormatted ?? '—'}
            </span>
            {saturatedBuckets.length > 0 ? (
              <StatusPill tone="warning">
                1.6 GB Spillover Active ({saturatedBuckets.length} bucket
                {saturatedBuckets.length === 1 ? '' : 's'} saturated)
              </StatusPill>
            ) : (
              <StatusPill tone="ok">
                {(snapshot?.buckets ?? []).filter((b) => b.sizeBytes > 0).length}{' '}
                active buckets
              </StatusPill>
            )}
          </div>
          <span className="ds-hint" style={{ marginTop: 'auto' }}>
            2 GB offset-limit guard &amp; automatic spillover active.
          </span>
        </section>

        <section className="ds-glass ds-card ds-stack-sm">
          <div className="ds-row-between">
            <h2 className="ds-label" style={{ margin: 0 }}>
              Backup Snapshot
            </h2>
            <Button
              variant="secondary"
              size="sm"
              disabled={!hasBackupPath}
              onClick={() =>
                void runCacheOp('openBackup', () =>
                  bridge.cache.openBackupFolder()
                )
              }
            >
              <FolderOpen size={14} strokeWidth={2} aria-hidden="true" />
              <span>Open Backup Folder</span>
            </Button>
          </div>
          <div className="ds-row-between">
            <span className="ds-metric-value">
              {snapshot?.backupSizeFormatted ?? 'N/A'}
            </span>
            {snapshot?.backupFolderStatus && (
              <StatusPill
                tone={getBackupStatusTone(snapshot.backupFolderStatus.status)}
              >
                {snapshot.backupFolderStatus.message}
              </StatusPill>
            )}
          </div>
          {progress !== null ? (
            <div className="ds-stack-sm">
              <label htmlFor="cache-op-progress" className="ds-label">
                {progress.operation === 'restore'
                  ? 'Restoring backup...'
                  : 'Backing up cache...'}{' '}
                ({progress.percent}%)
              </label>
              <progress
                id="cache-op-progress"
                className="ds-progress ds-progress-ok"
                value={progress.percent}
                max={100}
              />
            </div>
          ) : (
            <>
              <span className="ds-hint">
                Last backup:{' '}
                {formatLastBackup(snapshot?.lastBackupDateTimeIso ?? null)}
                {lastFeedback ? ` · ${lastFeedback}` : ''}
              </span>
              <span className="ds-hint ds-mono ds-ellipsis">
                <Archive size={12} strokeWidth={2} aria-hidden="true" style={{ verticalAlign: '-1px', marginRight: 6 }} />
                {snapshot?.backupFolderPath || 'No backup folder configured'}
              </span>
            </>
          )}
        </section>
      </div>

      {snapshot && snapshot.buckets.some((b) => b.sizeBytes > 0) && (
        <GlassPanel
          title="Thumbnail Cache Buckets"
          actions={
            <StatusPill tone={saturatedBuckets.length > 0 ? 'warning' : 'neutral'}>
              {snapshot.buckets.filter((b) => b.sizeBytes > 0).length} Active
            </StatusPill>
          }
        >
          <div className="ds-bucket-grid">
            {snapshot.buckets
              .filter((b) => b.sizeBytes > 0)
              .map((bucket) => (
                <div
                  key={bucket.size}
                  className="ds-bucket-item"
                  data-saturated={bucket.saturated}
                >
                  <span className="ds-label">
                    {bucket.size}
                    {bucket.saturated ? ' · Spillover' : ''}
                  </span>
                  <span className="ds-resource-name">
                    {formatBucketSizeMb(bucket.sizeBytes)}
                  </span>
                </div>
              ))}
          </div>
        </GlassPanel>
      )}

      <GlassPanel title="Automation &amp; Protection">
        <Switch
          label="Start with Windows"
          hint="Launch WinThumbsPreloader minimized to the tray at user sign-in."
          checked={snapshot?.startWithWindows ?? false}
          disabled={busyAction !== null}
          onChange={(checked) =>
            void runCacheOp('startup', () =>
              bridge.cache.setStartWithWindows(checked)
            )
          }
        />
        <hr className="ds-divider" />
        <Switch
          label="Auto Backup"
          hint={
            hasBackupPath
              ? 'Periodically back up Explorer thumbcache files whenever the cache grows.'
              : 'Select and save a backup folder below to enable Auto Backup.'
          }
          checked={snapshot?.autoBackup ?? false}
          disabled={!hasBackupPath || busyAction !== null}
          onChange={(checked) =>
            void runCacheOp('autoBackup', () =>
              bridge.cache.setAutoFlags({ autoBackup: checked })
            )
          }
        />
        <hr className="ds-divider" />
        <Switch
          label="Auto Restore"
          hint={
            hasBackupPath
              ? 'Automatically restore thumbnail cache databases when Explorer shrinks below backup.'
              : 'Select and save a backup folder below to enable Auto Restore.'
          }
          checked={snapshot?.autoRestore ?? false}
          disabled={!hasBackupPath || busyAction !== null}
          onChange={(checked) =>
            void runCacheOp('autoRestore', () =>
              bridge.cache.setAutoFlags({ autoRestore: checked })
            )
          }
        />
        <hr className="ds-divider" />
        <Switch
          label="Alert on Cache Reset"
          hint="Show an alert when Windows wipes or shrinks the Explorer thumbnail cache."
          checked={snapshot?.alertOnReset ?? false}
          disabled={busyAction !== null}
          onChange={(checked) =>
            void runCacheOp('alertOnReset', () =>
              bridge.cache.setAutoFlags({ alertOnReset: checked })
            )
          }
        />
        <hr className="ds-divider" />
        <Switch
          label="Auto Backup After Preload"
          hint="Trigger a cache backup right after a preload run finishes."
          checked={snapshot?.autoBackupAfterPreload ?? true}
          disabled={busyAction !== null}
          onChange={(checked) =>
            void runCacheOp('autoBackupPreload', () =>
              bridge.cache.saveAdvanced({ autoBackupAfterPreload: checked })
            )
          }
        />
      </GlassPanel>

      <GlassPanel title="Backup Location &amp; Advanced Policy">
        <div className="ds-field">
          <label htmlFor={backupFolderId} className="ds-label">
            Backup Folder Path
          </label>
          <div className="ds-row">
            <input
              id={backupFolderId}
              type="text"
              className="ds-input ds-mono"
              style={{ flex: '1 1 300px' }}
              value={backupFolderInput}
              onChange={(e) => {
                backupPathDirtyRef.current = true;
                setBackupFolderInput(e.target.value);
              }}
              placeholder="Choose a writable backup folder..."
            />
            <Button
              variant="secondary"
              onClick={() => void handlePickBackupFolder()}
            >
              Browse
            </Button>
            <Button
              variant="secondary"
              onClick={() =>
                void handleSetBackupFolder(
                  snapshot?.recommendedDefaultBackupPath ?? '',
                  false
                )
              }
            >
              Use Recommended Default
            </Button>
            <Button
              variant="primary"
              onClick={() => void handleSetBackupFolder(backupFolderInput, false)}
            >
              Save Folder
            </Button>
            <Button
              variant="ghost"
              onClick={() => void handleSetBackupFolder('', false)}
            >
              Clear Path
            </Button>
          </div>
        </div>

        <hr className="ds-divider" />

        <div className="ds-grid-2">
          <div className="ds-field">
            <label htmlFor={safetyId} className="ds-label">
              Backup / Restore Safety
            </label>
            <select
              id={safetyId}
              className="ds-select"
              value={safetyMode}
              onChange={(e) => {
                policyDirtyRef.current = true;
                setSafetyMode(e.target.value as BackupRestoreSafetyMode);
              }}
            >
              <option value="Allow Partial Copy">Allow Partial Copy</option>
              <option value="Require Full Copy">Require Full Copy</option>
            </select>
          </div>

          <div className="ds-field">
            <label htmlFor={deletionId} className="ds-label">
              Deletion Method
            </label>
            <select
              id={deletionId}
              className="ds-select"
              value={deletionMethod}
              onChange={(e) => {
                policyDirtyRef.current = true;
                setDeletionMethod(e.target.value as ExplorerDeletionMethod);
              }}
            >
              <option value="Manual Deletion">Manual Deletion</option>
              <option value="Disk Cleanup">Disk Cleanup</option>
            </select>
          </div>

          <div className="ds-field">
            <label htmlFor={closeFreqId} className="ds-label">
              Explorer Close Frequency
            </label>
            <select
              id={closeFreqId}
              className="ds-select"
              value={closeFrequency}
              onChange={(e) => {
                policyDirtyRef.current = true;
                setCloseFrequency(e.target.value as ExplorerCloseFrequency);
              }}
            >
              <option value="On Every File">On Every File</option>
              <option value="On Deletion Start">On Deletion Start</option>
            </select>
          </div>

          <div className="ds-field">
            <label htmlFor={formatId} className="ds-label">
              Size Display Format
            </label>
            <select
              id={formatId}
              className="ds-select"
              value={sizeFormat}
              onChange={(e) => {
                policyDirtyRef.current = true;
                setSizeFormat(e.target.value as CacheSizeFormat);
              }}
            >
              <option value="Auto">Auto</option>
              <option value="KB">KB</option>
              <option value="MB">MB</option>
              <option value="GB">GB</option>
            </select>
          </div>

          <div className="ds-field">
            <label htmlFor={refreshId} className="ds-label">
              Refresh Interval (50-60000 ms)
            </label>
            <DraftNumberInput
              id={refreshId}
              min={50}
              max={60000}
              fallback={250}
              className="ds-input"
              value={refreshIntervalMs}
              onCommit={(val) => {
                policyDirtyRef.current = true;
                setRefreshIntervalMs(val);
              }}
            />
          </div>

          <div className="ds-field">
            <label htmlFor={autoBackupIntId} className="ds-label">
              Auto Backup Interval (1-3600 s)
            </label>
            <DraftNumberInput
              id={autoBackupIntId}
              min={1}
              max={3600}
              fallback={5}
              className="ds-input"
              value={autoBackupIntervalSec}
              onCommit={(val) => {
                policyDirtyRef.current = true;
                setAutoBackupIntervalSec(val);
              }}
            />
          </div>

          <div className="ds-field">
            <label htmlFor={autoRestoreIntId} className="ds-label">
              Auto Restore Interval (1-3600 s)
            </label>
            <DraftNumberInput
              id={autoRestoreIntId}
              min={1}
              max={3600}
              fallback={5}
              className="ds-input"
              value={autoRestoreIntervalSec}
              onCommit={(val) => {
                policyDirtyRef.current = true;
                setAutoRestoreIntervalSec(val);
              }}
            />
          </div>
        </div>

        <div className="ds-row-between">
          <span className="ds-hint">
            Policy settings apply immediately to background telemetry, auto
            backup, and Explorer cache clearing.
          </span>
          <Button
            variant="primary"
            loading={busyAction === 'savePolicy'}
            onClick={() => void handleSavePolicy()}
          >
            <Save size={14} strokeWidth={2} aria-hidden="true" />
            <span>Save Policy</span>
          </Button>
        </div>
      </GlassPanel>

      <section className="ds-danger-zone" aria-label="Maintenance and danger zone">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Maintenance &amp; Danger Zone</h2>
          <p className="ds-hint">
            Destructive operations permanently delete local thumbnail cache
            databases, backup snapshots, or custom cache policies.
          </p>
        </div>

        <div className="ds-row">
          <select
            id={resetScopeId}
            aria-label="Reset scope"
            className="ds-select"
            style={{ width: 'auto', minBlockSize: 34, padding: '6px 12px' }}
            value={resetScope}
            onChange={(e) =>
              setResetScope(
                e.target.value as 'all' | 'general' | 'cache' | 'backup'
              )
            }
          >
            <option value="all">All Policy Settings</option>
            <option value="general">Intervals &amp; Format</option>
            <option value="cache">Cache Behavior</option>
            <option value="backup">Backup Location</option>
          </select>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setConfirmAction('resetDefaults')}
          >
            <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Reset Defaults</span>
          </Button>
          <Button
            variant="danger"
            size="sm"
            disabled={busyAction !== null || !hasBackupPath}
            loading={busyAction === 'clearBackup'}
            onClick={() => setConfirmAction('clearBackup')}
          >
            <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
            <span>Clear Backup</span>
          </Button>
          <Button
            variant="danger"
            size="sm"
            disabled={busyAction !== null}
            loading={busyAction === 'clearCache'}
            onClick={() => setConfirmAction('clearCache')}
          >
            <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
            <span>Clear Explorer Cache</span>
          </Button>
        </div>
      </section>

      <ConfirmDialog
        open={confirmAction === 'backupOverwrite'}
        title="Overwrite larger backup?"
        description={`Your saved backup (${snapshot?.backupSizeFormatted ?? 'N/A'}) is larger than your current Explorer thumbnail cache (${snapshot?.explorerSizeFormatted ?? 'N/A'}). Backing up now will replace that larger backup.`}
        confirmLabel="Backup Anyway"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runCacheOp('backup', () => bridge.cache.backup());
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'restore'}
        title="Restore backup over Explorer cache?"
        description="This will copy your saved thumbcache_*.db backup files into Windows Explorer's cache directory."
        confirmLabel="Restore Backup"
        onConfirm={() => {
          setConfirmAction(null);
          void runCacheOp('restore', () => bridge.cache.restore(false));
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'forceRestore'}
        title="Force restore and restart Windows Explorer?"
        description="Windows Explorer and COM thumbnail surrogate processes (dllhost.exe) will be restarted to release locked thumbcache files. Open Explorer windows will close."
        confirmLabel="Force Restore"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runCacheOp('forceRestore', () => bridge.cache.restore(true));
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'clearBackup'}
        title="Clear saved backup files?"
        description="All thumbcache_*.db files inside your backup folder will be permanently deleted."
        confirmLabel="Clear Backup"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runCacheOp('clearBackup', () => bridge.cache.clearBackup());
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'clearCache'}
        title="Clear Windows Explorer thumbnail cache?"
        description={`This deletes Explorer's local thumbcache_*.db files using "${deletionMethod}". Your backup snapshot will remain untouched.`}
        confirmLabel="Clear Explorer Cache"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runCacheOp('clearCache', () => bridge.cache.clearCache());
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'resetDefaults'}
        title="Reset cache policy defaults?"
        description={`Restore default settings for "${resetScope}"?`}
        confirmLabel="Reset Defaults"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          backupPathDirtyRef.current = false;
          policyDirtyRef.current = false;
          void runCacheOp('resetDefaults', () =>
            bridge.cache.resetAdvanced(resetScope)
          );
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={Boolean(pendingAdminFolder)}
        title="Administrator permissions may be required"
        description={
          pendingAdminFolder?.message ??
          'The selected backup folder is in a restricted location. Use this folder anyway?'
        }
        confirmLabel="Use Folder Anyway"
        destructive
        onConfirm={() => {
          const target = pendingAdminFolder?.path ?? '';
          setPendingAdminFolder(null);
          void handleSetBackupFolder(target, true);
        }}
        onCancel={() => setPendingAdminFolder(null)}
      />
    </div>
  );
}
