import { useRef, useState, type DragEvent } from 'react';
import {
  AlertTriangle,
  ArrowRight,
  Database,
  FolderOpen,
  FolderUp,
  RotateCcw,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import { resolveDroppedPreloadPaths } from '../lib/dropPaths';
import type {
  CacheProgressEvent,
  CacheSnapshot,
  CodecsStateDto,
  OptimizerStateDto,
  PreloadPathValidationItem,
  ScheduleState,
  ViewId,
} from '../lib/types';
import { Button } from '../components/ui/Button';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { StatusPill } from '../components/ui/StatusPill';

export interface OverviewViewProps {
  cacheSnapshot: CacheSnapshot | null;
  cacheProgress: CacheProgressEvent | null;
  codecsState: CodecsStateDto | null;
  optimizerState: OptimizerStateDto | null;
  scheduleState: ScheduleState | null;
  preloadQueueCount: number;
  onStagePreloadItems: (items: PreloadPathValidationItem[]) => void;
  onCacheSnapshotUpdated: (next: CacheSnapshot) => void;
  onNavigate: (view: ViewId) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

function formatLastBackupShort(iso: string | null): string {
  if (!iso) return 'Never backed up';
  try {
    const dt = new Date(iso);
    if (Number.isNaN(dt.getTime())) return iso;
    return dt.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

export function OverviewView({
  cacheSnapshot,
  cacheProgress,
  codecsState,
  optimizerState,
  scheduleState,
  preloadQueueCount,
  onStagePreloadItems,
  onCacheSnapshotUpdated,
  onNavigate,
  onNotify,
}: OverviewViewProps) {
  const [busyAction, setBusyAction] = useState<'backup' | 'restore' | null>(null);
  const [confirmOverwriteOpen, setConfirmOverwriteOpen] = useState(false);
  const [dragging, setDragging] = useState(false);
  const dragDepthRef = useRef(0);

  const activeBuckets = (cacheSnapshot?.buckets ?? []).filter(
    (b) => b.sizeBytes > 0
  ).length;
  const saturatedBuckets = (cacheSnapshot?.buckets ?? []).filter(
    (b) => b.saturated
  ).length;
  const hasBackupPath = Boolean(cacheSnapshot?.backupFolderPath?.trim());

  const activeCodecs = (codecsState?.families ?? []).filter(
    (f) => f.isEnabled || f.isPartiallyEnabled
  ).length;
  const totalCodecs = codecsState?.families?.length ?? 0;

  const optimizerTweaks = optimizerState?.tweaks ?? [];
  const appliedOptimizations = optimizerTweaks.filter((t) => t.enabled).length;
  const totalOptimizations = optimizerTweaks.length;

  const runCacheOp = async (
    kind: 'backup' | 'restore',
    action: () => ReturnType<typeof bridge.cache.backup>
  ) => {
    setBusyAction(kind);
    try {
      const res = await action();
      if (res.snapshot) {
        onCacheSnapshotUpdated(res.snapshot);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Cache operation failed.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleQuickPickFolders = async () => {
    try {
      const pick = await bridge.preload.pickFolder();
      if (pick.cancelled || pick.paths.length === 0) return;
      const res = await bridge.preload.validatePaths(pick.paths);
      onStagePreloadItems(res.items);
      onNavigate('preload');
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to browse folders.'
      );
    }
  };

  const handleQuickDrop = async (e: DragEvent<HTMLButtonElement>) => {
    e.preventDefault();
    dragDepthRef.current = 0;
    setDragging(false);
    try {
      const items = await resolveDroppedPreloadPaths(e.dataTransfer);
      if (items.length === 0) return;
      onStagePreloadItems(items);
      onNavigate('preload');
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to inspect dropped paths.'
      );
    }
  };

  const backupStatus = cacheSnapshot?.backupFolderStatus;
  const backupPillTone =
    backupStatus?.status === 'Writable' || backupStatus?.status === 'WillBeCreated'
      ? 'ok'
      : backupStatus?.status === 'WritableAdmin' ||
          backupStatus?.status === 'WillBeCreatedAdmin'
        ? 'warning'
        : backupStatus?.status === 'NotWritable' ||
            backupStatus?.status === 'InvalidPath'
          ? 'error'
          : 'neutral';

  const scheduleSummary = !scheduleState
    ? 'Checking status...'
    : !scheduleState.taskExists
      ? 'No automated Windows Task configured yet.'
      : scheduleState.nextRunTimeText ||
        scheduleState.statusMessage ||
        `Frequency: ${scheduleState.frequency || 'Day'}`;

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            System Overview
          </h1>
          <p className="ds-subtitle">
            Monitor Explorer thumbnail health, run a quick preload, or jump
            directly to system tools.
          </p>
        </div>

        <div className="ds-primary-actions">
          <Button variant="primary" onClick={() => onNavigate('preload')}>
            <FolderUp size={15} strokeWidth={2} aria-hidden="true" />
            <span>Quick Preload</span>
          </Button>
          <Button
            variant="secondary"
            loading={busyAction === 'backup'}
            loadingLabel="Backing Up..."
            disabled={busyAction !== null || !hasBackupPath}
            onClick={() => {
              if (cacheSnapshot?.backupLargerThanExplorer) {
                setConfirmOverwriteOpen(true);
              } else {
                void runCacheOp('backup', () => bridge.cache.backup());
              }
            }}
          >
            <Database size={14} strokeWidth={2} aria-hidden="true" />
            <span>Backup Cache Now</span>
          </Button>
          <Button variant="secondary" onClick={() => onNavigate('cache')}>
            <FolderOpen size={14} strokeWidth={2} aria-hidden="true" />
            <span>Manage Cache</span>
          </Button>
        </div>
      </div>

      {cacheSnapshot?.resetRecognized && (
        <div role="alert" className="ds-alert ds-alert-warning">
          <div className="ds-alert-body">
            <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" />
            <div>
              <strong>Thumbnail Cache Reset Detected:</strong> Windows shrunk
              the Explorer cache below your saved backup snapshot (
              {cacheSnapshot.explorerSizeFormatted} vs{' '}
              {cacheSnapshot.backupSizeFormatted}).
            </div>
          </div>
          <Button
            variant="primary"
            size="sm"
            loading={busyAction === 'restore'}
            disabled={busyAction !== null || !hasBackupPath}
            onClick={() =>
              void runCacheOp('restore', () => bridge.cache.restore(false))
            }
          >
            <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Restore Backup</span>
          </Button>
        </div>
      )}

      <div className="ds-grid-2">
        <section className="ds-glass ds-card ds-stack-sm">
          <div className="ds-row-between">
            <h2 className="ds-label" style={{ margin: 0 }}>
              Explorer Thumbnail Cache
            </h2>
            {saturatedBuckets > 0 ? (
              <StatusPill tone="warning">Spillover Active</StatusPill>
            ) : cacheSnapshot?.resetRecognized ? (
              <StatusPill tone="warning">Reset Alert</StatusPill>
            ) : (
              <StatusPill tone="ok">Protected</StatusPill>
            )}
          </div>
          <div className="ds-row-between">
            <span className="ds-metric-value">
              {cacheSnapshot?.explorerSizeFormatted ?? '—'}
            </span>
            <span className="ds-hint ds-mono">
              {activeBuckets} active .db buckets
            </span>
          </div>
          <progress
            className="ds-progress ds-progress-ok"
            value={cacheProgress ? cacheProgress.percent : 100}
            max={100}
            aria-label="Explorer cache protection status"
          />
          <p className="ds-hint">
            {cacheProgress
              ? `${cacheProgress.operation} in progress (${cacheProgress.percent}%)...`
              : '2 GB offset-limit guard active · Backup parity monitored'}
          </p>
        </section>

        <section className="ds-glass ds-card ds-stack-sm">
          <div className="ds-row-between">
            <h2 className="ds-label" style={{ margin: 0 }}>
              Backup Mirror Snapshot
            </h2>
            <StatusPill tone={backupPillTone}>
              {backupStatus?.status === 'Writable'
                ? 'Writable'
                : backupStatus?.status || 'Status'}
            </StatusPill>
          </div>
          <div className="ds-row-between">
            <span className="ds-metric-value">
              {cacheSnapshot?.backupSizeFormatted ?? 'N/A'}
            </span>
            <span className="ds-hint">
              Last backup:{' '}
              {formatLastBackupShort(cacheSnapshot?.lastBackupDateTimeIso ?? null)}
            </span>
          </div>
          <span className="ds-hint ds-mono ds-ellipsis">
            {cacheSnapshot?.backupFolderPath || 'No backup folder configured'}
          </span>
          <p className="ds-hint">
            {cacheSnapshot?.autoBackupAfterPreload
              ? 'Auto-backup triggers automatically after each preload run'
              : 'Manual or interval backup policy active'}
          </p>
        </section>
      </div>

      <button
        type="button"
        className="ds-overview-dropzone"
        data-dragging={dragging}
        onClick={() => void handleQuickPickFolders()}
        onDragEnter={(e) => {
          e.preventDefault();
          dragDepthRef.current += 1;
          setDragging(true);
        }}
        onDragOver={(e) => {
          e.preventDefault();
          if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
          setDragging(true);
        }}
        onDragLeave={() => {
          dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
          if (dragDepthRef.current === 0) setDragging(false);
        }}
        onDrop={(e) => void handleQuickDrop(e)}
      >
        <div className="ds-overview-dropzone-lead">
          <span className="ds-overview-dropzone-icon" aria-hidden="true">
            <FolderUp size={18} strokeWidth={2} />
          </span>
          <div>
            <div className="ds-resource-name">
              Drop folders, drives, or \\NAS shares here to stage a Preload batch
            </div>
            <div className="ds-hint">
              {preloadQueueCount > 0
                ? `${preloadQueueCount} folder${preloadQueueCount === 1 ? '' : 's'} currently staged in queue`
                : 'Click to browse folders and jump directly to Preload Queue'}
            </div>
          </div>
        </div>
        <span className="ds-button ds-button-sm" aria-hidden="true">
          <span>Open Queue</span>
          <ArrowRight size={13} strokeWidth={2} />
        </span>
      </button>

      <div className="ds-grid-3">
        <button
          type="button"
          className="ds-overview-card"
          onClick={() => onNavigate('codecs')}
        >
          <div className="ds-row-between">
            <span className="ds-label">Codecs</span>
            <StatusPill tone={activeCodecs > 0 ? 'ok' : 'neutral'}>
              {totalCodecs > 0 ? `${activeCodecs}/${totalCodecs} Active` : '—'}
            </StatusPill>
          </div>
          <span className="ds-overview-card-title">Native COM Providers</span>
          <span className="ds-hint">
            PSD, PDF, AI, EPS, INDD, XD, SVG, CDR, Affinity &amp; Camera RAW.
          </span>
          <span className="ds-overview-card-cta">
            <span>Manage Codecs</span>
            <ArrowRight size={13} strokeWidth={2} aria-hidden="true" />
          </span>
        </button>

        <button
          type="button"
          className="ds-overview-card"
          onClick={() => onNavigate('optimizer')}
        >
          <div className="ds-row-between">
            <span className="ds-label">Optimizer</span>
            <StatusPill tone={appliedOptimizations > 0 ? 'ok' : 'neutral'}>
              {totalOptimizations > 0
                ? `${appliedOptimizations}/${totalOptimizations} Applied`
                : '—'}
            </StatusPill>
          </div>
          <span className="ds-overview-card-title">Explorer Policies</span>
          <span className="ds-hint">
            {optimizerState?.enforceOnStartup
              ? 'Cache cleanup protection enabled with startup enforcement ON.'
              : 'Configure cache reset defenses and Explorer Icon Cache sizing.'}
          </span>
          <span className="ds-overview-card-cta">
            <span>Review Policies</span>
            <ArrowRight size={13} strokeWidth={2} aria-hidden="true" />
          </span>
        </button>

        <button
          type="button"
          className="ds-overview-card"
          onClick={() => onNavigate('schedule')}
        >
          <div className="ds-row-between">
            <span className="ds-label">Schedule</span>
            <StatusPill
              tone={
                !scheduleState?.taskExists
                  ? 'neutral'
                  : scheduleState.enabled
                    ? 'ok'
                    : 'warning'
              }
            >
              {!scheduleState?.taskExists
                ? 'Not Scheduled'
                : scheduleState.enabled
                  ? 'Enabled'
                  : 'Disabled'}
            </StatusPill>
          </div>
          <span className="ds-overview-card-title">
            {scheduleState?.taskExists
              ? `${scheduleState.frequency || 'Daily'} Automation`
              : 'Task Scheduler'}
          </span>
          <span className="ds-hint">{scheduleSummary}</span>
          <span className="ds-overview-card-cta">
            <span>Edit Schedule</span>
            <ArrowRight size={13} strokeWidth={2} aria-hidden="true" />
          </span>
        </button>
      </div>

      <ConfirmDialog
        open={confirmOverwriteOpen}
        title="Overwrite larger backup?"
        description={`Your saved backup (${cacheSnapshot?.backupSizeFormatted ?? 'N/A'}) is larger than your current Explorer thumbnail cache (${cacheSnapshot?.explorerSizeFormatted ?? 'N/A'}). Backing up now will replace that larger backup.`}
        confirmLabel="Backup Anyway"
        destructive
        onConfirm={() => {
          setConfirmOverwriteOpen(false);
          void runCacheOp('backup', () => bridge.cache.backup());
        }}
        onCancel={() => setConfirmOverwriteOpen(false)}
      />
    </div>
  );
}
