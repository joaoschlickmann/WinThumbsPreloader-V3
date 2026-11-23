import { useEffect, useId, useMemo, useState, type DragEvent } from 'react';
import {
  AlertTriangle,
  FileDown,
  FileImage,
  FileUp,
  RotateCcw,
  Save,
  Sparkles,
  Trash2,
} from 'lucide-react';
import { bridge } from '../../lib/bridge';
import type {
  ExtensionsSettingsDto,
  SettingsStateDto,
  ViewId,
} from '../../lib/types';
import { Button } from '../../components/ui/Button';
import { GlassPanel } from '../../components/ui/GlassPanel';
import { ConfirmDialog } from '../../components/ui/ModalDialog';
import { StatusPill } from '../../components/ui/StatusPill';
import { Switch } from '../../components/ui/Switch';

export interface ExtensionsTabProps {
  extensions: ExtensionsSettingsDto;
  onNavigate: (view: ViewId) => void;
  onStateUpdated: (next: SettingsStateDto) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

const AUTO_FORMAT_OPTIONS = [
  { value: 'Disabled', label: 'Disabled' },
  { value: 'Vertically', label: 'Vertically' },
  { value: 'Commas', label: 'Commas' },
  { value: 'Spaces', label: 'Spaces' },
  { value: 'CommasAndSpaces', label: 'Commas and Spaces' },
] as const;

export function ExtensionsTab({
  extensions,
  onNavigate,
  onStateUpdated,
  onNotify,
}: ExtensionsTabProps) {
  const [preloadFolderIcons, setPreloadFolderIcons] = useState(
    extensions.preloadFolderIcons
  );
  const [preloadAllFolders, setPreloadAllFolders] = useState(
    extensions.preloadAllFolders
  );
  const [autoFormatting, setAutoFormatting] = useState(
    extensions.extensionsAutoFormatting
  );
  const [extensionsText, setExtensionsText] = useState(
    extensions.extensionsText
  );
  const [dragging, setDragging] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);

  const scopeId = useId();
  const formatId = useId();
  const textareaId = useId();

  useEffect(() => {
    setPreloadFolderIcons(extensions.preloadFolderIcons);
    setPreloadAllFolders(extensions.preloadAllFolders);
    setAutoFormatting(extensions.extensionsAutoFormatting);
    setExtensionsText(extensions.extensionsText);
  }, [extensions]);

  const extensionCount = useMemo(() => {
    return extensionsText
      .split(/[,\s\r\n]+/)
      .map((s) => s.trim())
      .filter(Boolean).length;
  }, [extensionsText]);

  const handleFormatNow = async () => {
    try {
      const res = await bridge.settings.organizeExtensions(
        extensionsText,
        autoFormatting
      );
      setExtensionsText(res.text);
      onNotify('info', 'Extensions formatted.');
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to format extensions.'
      );
    }
  };

  const handleSave = async () => {
    setBusyAction('save');
    try {
      const res = await bridge.settings.saveExtensions({
        preloadFolderIcons,
        preloadAllFolders,
        extensionsAutoFormatting: autoFormatting,
        extensionsText: extensionsText.slice(0, 10_000),
      });
      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to save extensions.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleRestoreDefaults = async () => {
    setBusyAction('restore');
    try {
      const res = await bridge.settings.restoreDefaultExtensions();
      if (res.state) {
        onStateUpdated(res.state);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to restore default extensions.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleImportFile = async () => {
    try {
      const res = await bridge.settings.importExtensions();
      if (res.cancelled) return;
      if (res.ok) {
        setExtensionsText(res.text);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to import extensions.'
      );
    }
  };

  const handleExportFile = async () => {
    try {
      const res = await bridge.settings.exportExtensions(extensionsText);
      if (res.cancelled) return;
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to export extensions.'
      );
    }
  };

  const handleDrop = async (event: DragEvent<HTMLTextAreaElement>) => {
    event.preventDefault();
    event.stopPropagation();
    setDragging(false);

    const droppedFiles = Array.from(event.dataTransfer?.files ?? []);
    if (droppedFiles.length > 0) {
      try {
        const res = await bridge.settings.importDroppedExtensions(droppedFiles);
        if (res.ok) {
          setExtensionsText(res.text);
        }
        onNotify(res.ok ? 'info' : 'error', res.message);
      } catch (err) {
        onNotify(
          'error',
          err instanceof Error
            ? err.message
            : 'Failed to read dropped extension file.'
        );
      }
      return;
    }

    const droppedText = event.dataTransfer?.getData('text/plain') ?? '';
    if (droppedText.trim()) {
      setExtensionsText(droppedText.slice(0, 10_000));
      onNotify('info', 'Extensions text updated from drop.');
    }
  };

  return (
    <div className="ds-stack-md">
      <GlassPanel
        title="Folder Icon &amp; Extension Scope"
        actions={
          <Button
            variant="secondary"
            size="sm"
            onClick={() => onNavigate('codecs')}
          >
            <FileImage size={14} strokeWidth={2} aria-hidden="true" />
            <span>Manage Codecs</span>
          </Button>
        }
      >
        <Switch
          label="Preload Folder Icons"
          hint="Generate folder preview icons alongside media thumbnails."
          checked={preloadFolderIcons}
          onChange={setPreloadFolderIcons}
        />

        <div className="ds-grid-2">
          <div className="ds-field">
            <label htmlFor={scopeId} className="ds-label">
              Folder Icon Scope
            </label>
            <select
              id={scopeId}
              className="ds-select"
              value={preloadAllFolders ? 'all' : 'matching'}
              onChange={(e) => setPreloadAllFolders(e.target.value === 'all')}
            >
              <option value="matching">
                Folders Containing Set Extensions
              </option>
              <option value="all">All Folders</option>
            </select>
          </div>

          <div className="ds-field">
            <label htmlFor={formatId} className="ds-label">
              Extensions Auto-Format
            </label>
            <div className="ds-row">
              <select
                id={formatId}
                className="ds-select"
                style={{ flex: 1 }}
                value={autoFormatting}
                onChange={(e) => setAutoFormatting(e.target.value)}
              >
                {AUTO_FORMAT_OPTIONS.map((opt) => (
                  <option key={opt.value} value={opt.value}>
                    {opt.label}
                  </option>
                ))}
              </select>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void handleFormatNow()}
              >
                <Sparkles size={14} strokeWidth={2} aria-hidden="true" />
                <span>Format Now</span>
              </Button>
            </div>
          </div>
        </div>

        {preloadAllFolders && (
          <div role="alert" className="ds-alert ds-alert-warning">
            <div className="ds-alert-body">
              <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" />
              <span>
                <strong>Performance Warning:</strong> Selecting &quot;All
                Folders&quot; queues every subdirectory including system folders,
                which can cause hangs or freezes.
              </span>
            </div>
          </div>
        )}
      </GlassPanel>

      <GlassPanel
        title="Tracked File Extensions"
        actions={
          <StatusPill tone="neutral">
            {extensionCount} extensions · {extensionsText.length}/10,000 chars
          </StatusPill>
        }
      >
        <div className="ds-field">
          <label htmlFor={textareaId} className="ds-label">
            Extensions List (Supports Drag-and-Drop .txt / .csv or Plain Text)
          </label>
          <textarea
            id={textareaId}
            className="ds-textarea ds-mono"
            rows={7}
            maxLength={10_000}
            value={extensionsText}
            style={
              dragging ? { borderColor: 'var(--ds-primary)' } : undefined
            }
            onChange={(e) => setExtensionsText(e.target.value.slice(0, 10_000))}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(e) => void handleDrop(e)}
            placeholder="jpg, png, webp, avif, psd, pdf..."
          />
        </div>

        <div className="ds-row-between">
          <div className="ds-row">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void handleImportFile()}
            >
              <FileUp size={14} strokeWidth={2} aria-hidden="true" />
              <span>Import File</span>
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void handleExportFile()}
            >
              <FileDown size={14} strokeWidth={2} aria-hidden="true" />
              <span>Export (.txt / .csv)</span>
            </Button>
            <Button
              variant="secondary"
              size="sm"
              loading={busyAction === 'restore'}
              disabled={busyAction !== null}
              onClick={() => void handleRestoreDefaults()}
            >
              <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
              <span>Restore Defaults</span>
            </Button>
          </div>

          <Button
            variant="primary"
            loading={busyAction === 'save'}
            loadingLabel="Saving..."
            disabled={busyAction !== null}
            onClick={() => void handleSave()}
          >
            <Save size={14} strokeWidth={2} aria-hidden="true" />
            <span>Save Extensions</span>
          </Button>
        </div>
      </GlassPanel>

      <section className="ds-danger-zone" aria-label="Clear tracked extensions">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Clear Tracked List</h2>
          <p className="ds-hint">
            Removes all file extensions from the editor buffer below.
          </p>
        </div>
        <Button
          variant="danger"
          size="sm"
          onClick={() => setConfirmClearOpen(true)}
          disabled={!extensionsText}
        >
          <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
          <span>Clear All</span>
        </Button>
      </section>

      <ConfirmDialog
        open={confirmClearOpen}
        title="Clear all extensions?"
        description="Removes all tracked file extensions from the editor. Click Save Extensions afterward to persist an empty list."
        confirmLabel="Clear All"
        destructive
        onConfirm={() => {
          setConfirmClearOpen(false);
          setExtensionsText('');
        }}
        onCancel={() => setConfirmClearOpen(false)}
      />
    </div>
  );
}
