import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckSquare,
  Database,
  FileImage,
  Search,
  Shield,
  ShieldAlert,
  Trash2,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import type {
  CodecFamilyDto,
  CodecsStateDto,
  ViewId,
} from '../lib/types';
import { Button } from '../components/ui/Button';
import { GlassPanel } from '../components/ui/GlassPanel';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { StatusPill, type StatusTone } from '../components/ui/StatusPill';
import { Switch } from '../components/ui/Switch';

export interface CodecsViewProps {
  onNavigate: (view: ViewId) => void;
  onCodecsUpdated: (next: CodecsStateDto) => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

function getStatusTone(family: CodecFamilyDto): StatusTone {
  if (family.isEnabled) return 'ok';
  if (family.isPartiallyEnabled || family.hasExternalHandler) return 'warning';
  return 'neutral';
}

export function CodecsView({
  onNavigate,
  onCodecsUpdated,
  onNotify,
}: CodecsViewProps) {
  const [state, setState] = useState<CodecsStateDto | null>(null);
  const [selectedMask, setSelectedMask] = useState(0);
  const [filterQuery, setFilterQuery] = useState('');
  const [syncExtensions, setSyncExtensions] = useState(true);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [scopeNotice, setScopeNotice] = useState<string | null>(null);

  const [pendingApplyMode, setPendingApplyMode] = useState<null | {
    elevateViaUac: boolean;
    conflicts: CodecFamilyDto[];
  }>(null);
  const [confirmUninstallOpen, setConfirmUninstallOpen] = useState(false);

  const searchId = useId();

  const loadState = useCallback(async () => {
    try {
      const next = await bridge.codecs.getState();
      setState(next);
      setSelectedMask(next.enabledMask);
      onCodecsUpdated(next);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to load thumbnail codecs.'
      );
    }
  }, [onCodecsUpdated, onNotify]);

  useEffect(() => {
    void loadState();
  }, [loadState]);

  const filteredFamilies = useMemo(() => {
    const families = state?.families ?? [];
    const q = filterQuery.trim().toLowerCase();
    if (!q) return families;
    return families.filter(
      (f) =>
        f.displayName.toLowerCase().includes(q) ||
        f.description.toLowerCase().includes(q) ||
        f.currentHandlerName.toLowerCase().includes(q) ||
        f.extensions.some((ext) => ext.toLowerCase().includes(q))
    );
  }, [filterQuery, state?.families]);

  const toggleBit = (bitIndex: number) => {
    setSelectedMask((prev) => prev ^ (1 << bitIndex));
  };

  const selectAll = () => {
    if (!state) return;
    const all = state.families.reduce(
      (mask, f) => mask | (1 << f.bitIndex),
      0
    );
    setSelectedMask(all);
  };

  const selectMissingOnly = () => {
    if (!state) return;
    const missing = state.families.reduce((mask, f) => {
      if (f.isEnabled || !f.hasExternalHandler) {
        return mask | (1 << f.bitIndex);
      }
      return mask;
    }, 0);
    setSelectedMask(missing);
  };

  const selectNone = () => {
    setSelectedMask(0);
  };

  const executeApply = async (elevateViaUac: boolean) => {
    setBusyAction(elevateViaUac ? 'elevateApply' : 'apply');
    setScopeNotice(null);
    try {
      const res = await bridge.codecs.apply(
        selectedMask,
        elevateViaUac,
        syncExtensions
      );
      if (res.state) {
        setState(res.state);
        setSelectedMask(res.state.enabledMask);
        onCodecsUpdated(res.state);
      }
      if (res.scopeNotice) {
        setScopeNotice(res.scopeNotice);
      }
      onNotify(
        res.ok ? 'info' : res.uacCancelled ? 'warning' : 'error',
        res.message
      );
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to apply codecs.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const requestApply = (elevateViaUac: boolean) => {
    const conflicts = (state?.families ?? []).filter(
      (f) =>
        (selectedMask & (1 << f.bitIndex)) !== 0 &&
        f.hasExternalHandler &&
        !f.isEnabled
    );
    if (conflicts.length > 0) {
      setPendingApplyMode({ elevateViaUac, conflicts });
      return;
    }
    void executeApply(elevateViaUac);
  };

  const executeUninstallAll = async () => {
    setConfirmUninstallOpen(false);
    setBusyAction('uninstall');
    setScopeNotice(null);
    try {
      const res = await bridge.codecs.uninstallAll();
      if (res.state) {
        setState(res.state);
        setSelectedMask(res.state.enabledMask);
        onCodecsUpdated(res.state);
      }
      onNotify(
        res.ok ? (res.machineUninstallSkipped ? 'warning' : 'info') : 'error',
        res.message
      );
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to uninstall codecs.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const bundleOk = state?.bundlePresent ?? true;
  const isElevated = state?.isElevated ?? false;
  const activeCount = (state?.families ?? []).filter(
    (f) => f.isEnabled || f.isPartiallyEnabled
  ).length;
  const totalCount = state?.families.length ?? 0;

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Thumbnail Codecs
          </h1>
          <p className="ds-subtitle">
            Register bundled native WIC &amp; IThumbnailProvider codecs for PSD,
            PDF, Illustrator, EPS, InDesign, XD, SVG, CorelDRAW, Affinity, and
            Camera RAW formats.
          </p>
          <div className="ds-row" style={{ marginTop: 10 }}>
            <StatusPill
              tone={isElevated ? 'ok' : 'neutral'}
              icon={<Shield size={13} strokeWidth={2} aria-hidden="true" />}
            >
              {isElevated ? 'Administrator (HKLM + HKCU)' : 'Standard User (HKCU)'}
            </StatusPill>
            <StatusPill tone={activeCount > 0 ? 'ok' : 'neutral'}>
              {activeCount}/{totalCount} Active
            </StatusPill>
          </div>
        </div>
        <div className="ds-primary-actions">
          <Button
            variant="primary"
            loading={busyAction === 'apply'}
            loadingLabel="Applying..."
            disabled={!bundleOk || busyAction !== null}
            onClick={() => requestApply(false)}
          >
            <span>Apply Selection</span>
          </Button>
          <Button
            variant="secondary"
            loading={busyAction === 'elevateApply'}
            disabled={!bundleOk || busyAction !== null}
            onClick={() => requestApply(!isElevated)}
          >
            <Shield size={14} strokeWidth={2} aria-hidden="true" />
            <span>
              {isElevated ? 'Apply System-Wide (HKLM)' : 'Elevate & Apply (UAC)'}
            </span>
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => onNavigate('cache')}
            disabled={busyAction !== null}
          >
            <Database size={14} strokeWidth={2} aria-hidden="true" />
            <span>Open Cache Manager</span>
          </Button>
        </div>
      </div>

      {!bundleOk && (
        <div role="alert" className="ds-alert">
          <div className="ds-alert-body">
            <ShieldAlert size={18} strokeWidth={2} aria-hidden="true" />
            <div>
              <strong>Bundled Codecs Missing:</strong>{' '}
              {state?.bundleError ||
                'WinThumbsCodecs.dll or pdfium.dll was not found in the Codecs directory.'}
            </div>
          </div>
        </div>
      )}

      {bundleOk && !isElevated && (
        <div role="status" className="ds-card ds-row-between">
          <div className="ds-row">
            <FileImage size={18} strokeWidth={2} aria-hidden="true" />
            <span className="ds-hint" style={{ margin: 0 }}>
              Bundled <span className="ds-mono">WinThumbsCodecs.dll</span> and{' '}
              <span className="ds-mono">pdfium.dll</span> verified. Use{' '}
              <strong>Elevate &amp; Apply (UAC)</strong> for system-wide
              (HKLM) and elevated preloader visibility.
            </span>
          </div>
        </div>
      )}

      {scopeNotice && (
        <div role="status" className="ds-alert ds-alert-warning">
          <div className="ds-alert-body">
            <AlertTriangle size={18} strokeWidth={2} aria-hidden="true" />
            <span>{scopeNotice}</span>
          </div>
        </div>
      )}

      <GlassPanel
        title="Codec Selection &amp; Sync"
        actions={
          <div className="ds-row">
            <Button variant="secondary" size="sm" onClick={selectAll}>
              <CheckSquare size={14} strokeWidth={2} aria-hidden="true" />
              <span>Select All</span>
            </Button>
            <Button variant="secondary" size="sm" onClick={selectMissingOnly}>
              Select Missing Only
            </Button>
            <Button variant="ghost" size="sm" onClick={selectNone}>
              Select None
            </Button>
          </div>
        }
      >
        <div className="ds-grid-2">
          <div className="ds-field">
            <label htmlFor={searchId} className="ds-label">
              Filter Codec Families or Extensions
            </label>
            <div className="ds-row">
              <Search size={16} strokeWidth={2} aria-hidden="true" />
              <input
                id={searchId}
                type="search"
                className="ds-input"
                style={{ flex: 1 }}
                value={filterQuery}
                onChange={(e) => setFilterQuery(e.target.value)}
                placeholder="Search by name, handler, or .extension (e.g., .psd, .cr3)"
              />
            </div>
          </div>

          <Switch
            label="Sync enabled formats to Preload Extensions"
            hint="Automatically merge newly enabled codec file extensions into Settings -> Extensions."
            checked={syncExtensions}
            onChange={setSyncExtensions}
          />
        </div>

        <hr className="ds-divider" />

        {filteredFamilies.length === 0 ? (
          <div className="ds-card ds-stack-sm" role="status">
            <p className="ds-resource-name" style={{ margin: 0 }}>
              No matching codec families
            </p>
            <p className="ds-hint">
              Clear or adjust your filter query to view all 10 bundled codec
              families.
            </p>
          </div>
        ) : (
          <div className="ds-grid-2">
            {filteredFamilies.map((family) => {
              const checked = (selectedMask & (1 << family.bitIndex)) !== 0;
              const checkId = `codec-family-${family.id}`;
              return (
                <article
                  key={family.id}
                  className="ds-codec-card"
                  data-selected={checked}
                >
                  <div className="ds-row-between">
                    <label htmlFor={checkId} className="ds-checkbox-label">
                      <input
                        id={checkId}
                        type="checkbox"
                        className="ds-checkbox"
                        checked={checked}
                        onChange={() => toggleBit(family.bitIndex)}
                      />
                      <span>{family.displayName}</span>
                    </label>
                    <div className="ds-row" style={{ gap: 6 }}>
                      {family.recommended && (
                        <StatusPill tone="neutral" showDefaultIcon={false}>
                          Recommended
                        </StatusPill>
                      )}
                      <StatusPill tone={getStatusTone(family)}>
                        {family.statusText}
                      </StatusPill>
                    </div>
                  </div>

                  <p className="ds-hint" style={{ margin: 0 }}>
                    {family.description}
                  </p>

                  <div className="ds-ext-chips" aria-label="Supported extensions">
                    {family.extensions.slice(0, 8).map((ext) => (
                      <span key={ext} className="ds-ext-chip">
                        {ext}
                      </span>
                    ))}
                    {family.extensions.length > 8 && (
                      <span className="ds-ext-chip">
                        +{family.extensions.length - 8} more
                      </span>
                    )}
                  </div>

                  <div className="ds-row-between">
                    <span className="ds-label">Current Handler</span>
                    <span className="ds-hint ds-ellipsis" style={{ margin: 0 }}>
                      {family.currentHandlerName}
                    </span>
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </GlassPanel>

      <section className="ds-danger-zone" aria-label="Codec unregistration">
        <div className="ds-danger-zone-meta">
          <h2 className="ds-danger-zone-title">Codec Unregistration</h2>
          <p className="ds-hint">
            Removes WinThumbsCodecs registrations and restores any previously
            backed-up third-party shell handlers.
          </p>
        </div>
        <Button
          variant="danger"
          size="sm"
          loading={busyAction === 'uninstall'}
          disabled={busyAction !== null}
          onClick={() => setConfirmUninstallOpen(true)}
        >
          <Trash2 size={14} strokeWidth={2} aria-hidden="true" />
          <span>Uninstall All</span>
        </Button>
      </section>

      <ConfirmDialog
        open={Boolean(pendingApplyMode)}
        title="Replace existing external thumbnail handlers?"
        description="One or more selected formats already have third-party shell handlers registered. WinThumbsPreloader will back them up before activating its built-in codecs so Uninstall All can restore them."
        confirmLabel="Back Up & Replace Handlers"
        onConfirm={() => {
          const mode = pendingApplyMode;
          setPendingApplyMode(null);
          if (mode) {
            void executeApply(mode.elevateViaUac);
          }
        }}
        onCancel={() => setPendingApplyMode(null)}
      >
        {pendingApplyMode && (
          <ul className="ds-code-list">
            {pendingApplyMode.conflicts.map((c) => (
              <li key={c.id}>
                {c.displayName}: {c.currentHandlerName}
              </li>
            ))}
          </ul>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmUninstallOpen}
        title="Uninstall all built-in codecs?"
        description="Removes WinThumbsCodecs registrations and restores any previously backed-up third-party thumbnail handlers."
        confirmLabel="Uninstall All"
        destructive
        onConfirm={() => void executeUninstallAll()}
        onCancel={() => setConfirmUninstallOpen(false)}
      />
    </div>
  );
}
