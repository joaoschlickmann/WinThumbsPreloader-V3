import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type DragEvent,
  type FormEvent,
} from 'react';
import {
  AlertCircle,
  Copy,
  FileDown,
  Folder,
  FolderPlus,
  Play,
  RotateCcw,
  Save,
  Server,
  Trash2,
  UploadCloud,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import { resolveDroppedPreloadPaths } from '../lib/dropPaths';
import type {
  BulkPreset,
  PreloadPathValidationItem,
} from '../lib/types';
import { Button } from '../components/ui/Button';
import { DraftNumberInput } from '../components/ui/DraftNumberInput';
import { GlassPanel } from '../components/ui/GlassPanel';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { StatusPill } from '../components/ui/StatusPill';
import { Switch } from '../components/ui/Switch';

export interface PreloadViewProps {
  initialLaunchWarning: string | null;
  onDismissLaunchWarning?: () => void;
  queue?: PreloadPathValidationItem[];
  onQueueChange?: (
    updater:
      | PreloadPathValidationItem[]
      | ((prev: PreloadPathValidationItem[]) => PreloadPathValidationItem[])
  ) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

export function PreloadView({
  initialLaunchWarning,
  onDismissLaunchWarning,
  queue: controlledQueue,
  onQueueChange,
  onNotify,
}: PreloadViewProps) {
  const [launchWarning, setLaunchWarning] = useState<string | null>(
    initialLaunchWarning
  );
  const [localQueue, setLocalQueue] = useState<PreloadPathValidationItem[]>([]);
  const queue = controlledQueue ?? localQueue;
  const setQueue = onQueueChange ?? setLocalQueue;
  const [pathInput, setPathInput] = useState('');
  const [dragging, setDragging] = useState(false);
  const dragDepthRef = useRef(0);

  const [recursive, setRecursive] = useState(true);
  const [multithreaded, setMultithreaded] = useState(true);
  const [threads, setThreads] = useState(0);
  const [method, setMethod] = useState<'Sequentially' | 'Simultaneously'>(
    'Sequentially'
  );
  const [maximumInstances, setMaximumInstances] = useState(0);

  const [presets, setPresets] = useState<BulkPreset[]>([]);
  const [presetName, setPresetName] = useState('');
  const [presetPendingDelete, setPresetPendingDelete] = useState<string | null>(
    null
  );

  const [starting, setStarting] = useState(false);
  const [savingPreset, setSavingPreset] = useState(false);
  const [confirmPartialRun, setConfirmPartialRun] = useState<{
    message: string;
    invalidPaths: string[];
  } | null>(null);

  const addPathId = useId();
  const threadsId = useId();
  const maxInstancesId = useId();
  const presetInputId = useId();

  useEffect(() => {
    setLaunchWarning(initialLaunchWarning);
  }, [initialLaunchWarning]);

  useEffect(() => {
    let active = true;
    bridge.preload
      .getState()
      .then((state) => {
        if (!active) return;
        setMultithreaded(state.defaultMultithreaded);
        setThreads(state.defaultThreadCount);
        setPresets(state.savedPresets);
        if (state.invalidLaunchPathWarning) {
          setLaunchWarning(state.invalidLaunchPathWarning);
        }
      })
      .catch((err) => {
        if (!active) return;
        onNotify(
          'error',
          err instanceof Error ? err.message : 'Failed to load preload state.'
        );
      });
    return () => {
      active = false;
    };
  }, [onNotify]);

  const mergeValidatedItems = useCallback(
    (incoming: PreloadPathValidationItem[]) => {
      if (incoming.length === 0) return;
      setQueue((prev) => {
        const next = [...prev];
        const seen = new Set(
          prev.map((item) =>
            (item.normalizedPath ?? item.input).toLowerCase()
          )
        );
        for (const item of incoming) {
          const key = (item.normalizedPath ?? item.input).toLowerCase();
          if (!key || seen.has(key)) continue;
          seen.add(key);
          next.push(item);
        }
        return next;
      });
    },
    []
  );

  const addRawPaths = useCallback(
    async (rawPaths: string[]) => {
      const cleaned = rawPaths
        .map((p) => p.trim().replace(/^"+|"+$/g, ''))
        .filter(Boolean);
      if (cleaned.length === 0) return;
      try {
        const res = await bridge.preload.validatePaths(cleaned);
        mergeValidatedItems(res.items);
      } catch (err) {
        onNotify(
          'error',
          err instanceof Error ? err.message : 'Failed to validate paths.'
        );
      }
    },
    [mergeValidatedItems, onNotify]
  );

  const handlePickFolders = async () => {
    try {
      const picked = await bridge.preload.pickFolder(true);
      if (!picked.cancelled && picked.paths.length > 0) {
        await addRawPaths(picked.paths);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Folder selection failed.'
      );
    }
  };

  const handleDrop = async (event: DragEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    dragDepthRef.current = 0;
    setDragging(false);

    try {
      const items = await resolveDroppedPreloadPaths(event.dataTransfer);
      if (items.length > 0) {
        mergeValidatedItems(items);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to resolve dropped items.'
      );
    }
  };

  const handleManualAdd = async (event: FormEvent) => {
    event.preventDefault();
    const lines = pathInput
      .split(/[\r\n]+/)
      .map((s) => s.trim())
      .filter(Boolean);
    if (lines.length === 0) return;
    await addRawPaths(lines);
    setPathInput('');
  };

  const handleCopyPath = async (value: string) => {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value);
        onNotify('info', 'Path copied to clipboard.');
      }
    } catch {
      onNotify('warning', 'Clipboard access is unavailable.');
    }
  };

  const handleRemovePath = (index: number) => {
    setQueue((prev) => prev.filter((_, idx) => idx !== index));
  };

  const handleClearQueue = () => {
    setQueue([]);
  };

  const handleExportPaths = async () => {
    const paths = queue.map((q) => q.normalizedPath ?? q.input);
    try {
      const res = await bridge.preload.exportPaths(paths);
      if (!res.cancelled) {
        onNotify(res.ok ? 'info' : 'error', res.message);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to export paths.'
      );
    }
  };

  const executeStart = async (skipInvalid: boolean) => {
    setStarting(true);
    try {
      const res = await bridge.preload.start({
        paths: queue.map((q) => q.normalizedPath ?? q.input),
        recursive,
        multithreaded,
        threads: Math.max(0, Math.min(256, Number(threads) || 0)),
        method,
        maximumInstances: Math.max(
          0,
          Math.min(128, Number(maximumInstances) || 0)
        ),
        skipInvalid,
      });

      if (res.requiresConfirmation) {
        setConfirmPartialRun({
          message: res.message,
          invalidPaths: res.invalidPaths,
        });
        return;
      }

      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to start preloader.'
      );
    } finally {
      setStarting(false);
    }
  };

  const handleSavePreset = async (event: FormEvent) => {
    event.preventDefault();
    const trimmed = presetName.trim();
    if (!trimmed) {
      onNotify('warning', 'Please enter a name for the preset.');
      return;
    }
    if (queue.length === 0) {
      onNotify('warning', 'Add at least one path before saving a preset.');
      return;
    }
    setSavingPreset(true);
    try {
      const res = await bridge.preload.savePreset({
        name: trimmed,
        paths: queue.map((q) => q.normalizedPath ?? q.input),
        recursive,
        multiThreaded: multithreaded,
        threads: Math.max(0, Math.min(256, Number(threads) || 0)),
        maximumInstances: Math.max(
          0,
          Math.min(128, Number(maximumInstances) || 0)
        ),
        method,
      });
      setPresets(res.savedPresets);
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to save preset.'
      );
    } finally {
      setSavingPreset(false);
    }
  };

  const handleLoadPreset = async (preset: BulkPreset) => {
    setPresetName(preset.name);
    setRecursive(preset.recursive);
    setMultithreaded(preset.multiThreaded);
    setThreads(preset.threads);
    setMethod(
      preset.method === 'Simultaneously' ? 'Simultaneously' : 'Sequentially'
    );
    setMaximumInstances(preset.maximumInstances);
    setQueue([]);
    await addRawPaths(preset.paths);
    onNotify('info', `Loaded preset '${preset.name}'.`);
  };

  const handleDeletePresetConfirmed = async () => {
    if (!presetPendingDelete) return;
    const target = presetPendingDelete;
    setPresetPendingDelete(null);
    try {
      const res = await bridge.preload.deletePreset(target);
      setPresets(res.savedPresets);
      if (presetName.trim().toLowerCase() === target.toLowerCase()) {
        setPresetName('');
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to delete preset.'
      );
    }
  };

  const handleExportPresets = async () => {
    try {
      const res = await bridge.preload.exportPresets();
      if (!res.cancelled) {
        onNotify(res.ok ? 'info' : 'error', res.message);
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to export presets.'
      );
    }
  };

  const validCount = useMemo(
    () => queue.filter((item) => item.valid).length,
    [queue]
  );

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Preload &amp; Batch Queue
          </h1>
          <p className="ds-subtitle">
            Stage folders, drives, or network shares to generate Windows Explorer
            thumbnails ahead of browsing.
          </p>
        </div>
        <div className="ds-primary-actions">
          <StatusPill tone={validCount > 0 ? 'ok' : 'neutral'}>
            {queue.length} Staged ({validCount} Valid)
          </StatusPill>
          <Button variant="secondary" onClick={handlePickFolders}>
            <FolderPlus size={14} strokeWidth={2} aria-hidden="true" />
            <span>Add Folder</span>
          </Button>
          <Button
            variant="primary"
            disabled={queue.length === 0}
            loading={starting}
            loadingLabel="Launching..."
            onClick={() => executeStart(false)}
          >
            <Play size={15} strokeWidth={2} aria-hidden="true" />
            <span>Start Preloading</span>
          </Button>
        </div>
      </div>

      {launchWarning && (
        <div role="alert" className="ds-alert">
          <div className="ds-alert-body">
            <AlertCircle size={18} strokeWidth={2} aria-hidden="true" />
            <div>
              <strong>Invalid command-line path ignored:</strong>{' '}
              <span className="ds-mono">{launchWarning}</span>
            </div>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => {
              setLaunchWarning(null);
              onDismissLaunchWarning?.();
            }}
          >
            Dismiss
          </Button>
        </div>
      )}

      <div className="ds-preload-layout">
        <div className="ds-stack-md">
          <button
            type="button"
            className="ds-dropzone"
            data-dragging={dragging}
            onClick={handlePickFolders}
            onDragEnter={(e) => {
              e.preventDefault();
              dragDepthRef.current += 1;
              setDragging(true);
            }}
            onDragOver={(e) => {
              e.preventDefault();
              if (e.dataTransfer) {
                e.dataTransfer.dropEffect = 'copy';
              }
              setDragging(true);
            }}
            onDragLeave={() => {
              dragDepthRef.current = Math.max(0, dragDepthRef.current - 1);
              if (dragDepthRef.current === 0) {
                setDragging(false);
              }
            }}
            onDrop={handleDrop}
          >
            <span className="ds-dropzone-icon-wrap" aria-hidden="true">
              <UploadCloud size={28} strokeWidth={2} />
            </span>
            <span className="ds-dropzone-title">
              Drop folders here or click to browse
            </span>
            <span className="ds-dropzone-sub">
              Supports local drives, mapped letters, and UNC network shares
            </span>
          </button>

          <GlassPanel
            title="Staged Paths"
            actions={
              <Button
                variant="secondary"
                size="sm"
                onClick={handleExportPaths}
                disabled={queue.length === 0}
              >
                <FileDown size={14} strokeWidth={2} aria-hidden="true" />
                <span>Export Paths (.txt)</span>
              </Button>
            }
          >
            <form onSubmit={handleManualAdd} className="ds-row">
              <div className="ds-field" style={{ flex: '1 1 260px' }}>
                <label htmlFor={addPathId} className="ds-label">
                  Add Path Manually
                </label>
                <input
                  id={addPathId}
                  type="text"
                  className="ds-input ds-mono"
                  value={pathInput}
                  onChange={(e) => setPathInput(e.target.value)}
                  placeholder="D:\Photos or \\NAS\Media"
                />
              </div>
              <Button
                type="submit"
                variant="secondary"
                disabled={!pathInput.trim()}
                style={{ alignSelf: 'flex-end' }}
              >
                Add Path
              </Button>
            </form>

            {queue.length === 0 ? (
              <div className="ds-card ds-stack-sm" role="status">
                <p className="ds-resource-name" style={{ margin: 0 }}>
                  No folders staged yet
                </p>
                <p className="ds-hint">
                  Drop folders onto the dropzone above, click Add Folder, or
                  enter a path to build a preload batch.
                </p>
              </div>
            ) : (
              <ul className="ds-list">
                {queue.map((item, idx) => {
                  const displayPath = item.normalizedPath || item.input;
                  return (
                    <li key={`${displayPath}-${idx}`} className="ds-resource-item">
                      <div className="ds-resource-lead">
                        <span className="ds-resource-avatar" aria-hidden="true">
                          {!item.valid ? (
                            <AlertCircle size={18} strokeWidth={2} />
                          ) : item.isNetwork ? (
                            <Server size={18} strokeWidth={2} />
                          ) : (
                            <Folder size={18} strokeWidth={2} />
                          )}
                        </span>
                        <div className="ds-stack-sm" style={{ minWidth: 0, flex: 1 }}>
                          <span className="ds-resource-name ds-mono ds-ellipsis">
                            {displayPath}
                          </span>
                          <div className="ds-row">
                            <StatusPill
                              tone={item.valid ? 'active' : 'error'}
                            >
                              {item.valid ? 'Valid' : 'Invalid'}
                            </StatusPill>
                            {item.isNetwork && (
                              <StatusPill tone="warning">
                                Network/UNC
                              </StatusPill>
                            )}
                          </div>
                        </div>
                      </div>

                      <div className="ds-resource-actions">
                        <Button
                          variant="ghost"
                          size="icon"
                          aria-label={`Copy ${displayPath}`}
                          onClick={() => handleCopyPath(displayPath)}
                        >
                          <Copy size={15} strokeWidth={2} aria-hidden="true" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          aria-label={`Remove ${displayPath}`}
                          onClick={() => handleRemovePath(idx)}
                        >
                          <Trash2 size={15} strokeWidth={2} aria-hidden="true" />
                        </Button>
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}

            <hr className="ds-divider" />

            <div className="ds-stack-sm">
              <h3 className="ds-section-title">Execution Options</h3>
              <Switch
                label="Include Nested Directories (Recursive)"
                hint="Preload thumbnails inside all subdirectories of each staged folder."
                checked={recursive}
                onChange={setRecursive}
              />

              <Switch
                label="Multi-Threaded"
                hint="Process thumbnails in parallel across multiple worker threads."
                checked={multithreaded}
                onChange={setMultithreaded}
              />

              <div className="ds-grid-2">
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
                      <RotateCcw size={13} strokeWidth={2} aria-hidden="true" />
                      <span>Auto</span>
                    </Button>
                  </div>
                </div>

                <div className="ds-field">
                  <span className="ds-label">Execution Mode</span>
                  <SegmentedControl
                    ariaLabel="Execution Mode"
                    value={method}
                    onChange={setMethod}
                    options={[
                      { value: 'Sequentially', label: 'Sequential' },
                      { value: 'Simultaneously', label: 'Simultaneous' },
                    ]}
                  />
                </div>
              </div>

              {method === 'Simultaneously' && (
                <div className="ds-field">
                  <label htmlFor={maxInstancesId} className="ds-label">
                    Max Parallel Instances (0 = Auto, 0-128)
                  </label>
                  <div className="ds-row">
                    <DraftNumberInput
                      id={maxInstancesId}
                      min={0}
                      max={128}
                      fallback={0}
                      className="ds-input"
                      style={{ maxWidth: 220 }}
                      value={maximumInstances}
                      onCommit={setMaximumInstances}
                    />
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => setMaximumInstances(0)}
                    >
                      Unlimited
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </GlassPanel>
        </div>

        <GlassPanel
          title="Saved Presets"
          subtitle="Reusable folder queues and concurrency profiles."
          actions={
            <Button
              variant="secondary"
              size="sm"
              onClick={handleExportPresets}
              disabled={presets.length === 0}
            >
              <FileDown size={14} strokeWidth={2} aria-hidden="true" />
              <span>Export All (.json)</span>
            </Button>
          }
        >
          <form onSubmit={handleSavePreset} className="ds-stack-sm">
            <label htmlFor={presetInputId} className="ds-label">
              Save Current As...
            </label>
            <div className="ds-row">
              <input
                id={presetInputId}
                type="text"
                className="ds-input"
                style={{ flex: 1 }}
                value={presetName}
                onChange={(e) => setPresetName(e.target.value)}
                placeholder="Preset name"
              />
              <Button
                type="submit"
                variant="primary"
                size="sm"
                loading={savingPreset}
              >
                <Save size={14} strokeWidth={2} aria-hidden="true" />
                <span>Save</span>
              </Button>
            </div>
          </form>

          <hr className="ds-divider" />

          {presets.length === 0 ? (
            <div className="ds-card ds-stack-sm" role="status">
              <p className="ds-resource-name" style={{ margin: 0 }}>
                No saved presets
              </p>
              <p className="ds-hint">
                Stage folders on the left and enter a preset name above to save
                them for one-click reuse.
              </p>
            </div>
          ) : (
            <ul className="ds-list">
              {presets.map((preset) => (
                <li key={preset.name} className="ds-resource-item">
                  <div className="ds-stack-sm" style={{ minWidth: 0, flex: 1 }}>
                    <div className="ds-row">
                      <span className="ds-resource-name ds-ellipsis">
                        {preset.name}
                      </span>
                      <StatusPill tone="neutral" showDefaultIcon={false}>
                        {preset.paths.length}{' '}
                        {preset.paths.length === 1 ? 'path' : 'paths'}
                      </StatusPill>
                    </div>
                    <span className="ds-resource-sub">
                      {preset.method} ·{' '}
                      {preset.recursive ? 'Recursive' : 'Top-level'} ·{' '}
                      {preset.multiThreaded
                        ? preset.threads > 0
                          ? `${preset.threads} threads`
                          : 'Auto threads'
                        : 'Single-thread'}
                    </span>
                  </div>

                  <div className="ds-resource-actions">
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => handleLoadPreset(preset)}
                    >
                      Load
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Delete preset ${preset.name}`}
                      onClick={() => setPresetPendingDelete(preset.name)}
                    >
                      <Trash2 size={15} strokeWidth={2} aria-hidden="true" />
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </GlassPanel>
      </div>

      <section className="ds-danger-zone" aria-label="Queue cleanup">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Queue Cleanup</h2>
          <p className="ds-hint">
            Clear all staged folders from the current batch queue without removing saved presets.
          </p>
        </div>
        <Button
          variant="danger"
          size="sm"
          onClick={handleClearQueue}
          disabled={queue.length === 0}
        >
          <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
          <span>Clear All</span>
        </Button>
      </section>

      <ConfirmDialog
        open={Boolean(confirmPartialRun)}
        title="Some paths could not be accessed"
        description={
          confirmPartialRun?.message ??
          'Some staged paths were invalid. Continue with the remaining valid paths?'
        }
        confirmLabel="Continue with valid paths"
        onConfirm={() => {
          setConfirmPartialRun(null);
          void executeStart(true);
        }}
        onCancel={() => setConfirmPartialRun(null)}
      >
        {confirmPartialRun && confirmPartialRun.invalidPaths.length > 0 && (
          <ul className="ds-code-list">
            {confirmPartialRun.invalidPaths.map((p) => (
              <li key={p}>{p}</li>
            ))}
          </ul>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={Boolean(presetPendingDelete)}
        title="Delete saved preset?"
        description={`Remove '${presetPendingDelete ?? ''}' from your saved preload presets?`}
        confirmLabel="Delete Preset"
        destructive
        onConfirm={() => void handleDeletePresetConfirmed()}
        onCancel={() => setPresetPendingDelete(null)}
      />
    </div>
  );
}
