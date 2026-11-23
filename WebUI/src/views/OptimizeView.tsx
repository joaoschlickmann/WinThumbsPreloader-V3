import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import {
  ChevronDown,
  ChevronRight,
  FolderCog,
  RefreshCw,
  RotateCcw,
  Shield,
  Sparkles,
  Wrench,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import type {
  OptimizerProfileDto,
  OptimizerStateDto,
  OptimizerTweakDto,
} from '../lib/types';
import { Button } from '../components/ui/Button';
import { GlassPanel } from '../components/ui/GlassPanel';
import { ConfirmDialog } from '../components/ui/ModalDialog';
import { SegmentedControl } from '../components/ui/SegmentedControl';
import { StatusPill, type StatusTone } from '../components/ui/StatusPill';
import { Switch } from '../components/ui/Switch';

export interface OptimizeViewProps {
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

type OptimizerTab = 'cacheNetwork' | 'explorer' | 'shell';

const PROFILE_KEYS: Record<string, keyof OptimizerProfileDto> = {
  enforceOnStartup: 'enforceOnStartup',
  preventThumbCacheCleanup: 'preventThumbCacheCleanup',
  clearDiskCleanupFlags: 'clearDiskCleanupFlags',
  disableSilentCleanupTask: 'disableSilentCleanupTask',
  disableNetworkThumbsDb: 'disableNetworkThumbsDb',
  forceThumbnailPreviews: 'forceThumbnailPreviews',
  disableFolderTypeDiscovery: 'disableFolderTypeDiscovery',
  expandBagMruSize: 'expandBagMruSize',
  expandIconCache: 'expandIconCache',
};

function getTweakStatusTone(stateText: string): StatusTone {
  if (stateText === 'Optimized') return 'active';
  if (stateText === 'PartiallyOptimized') return 'warning';
  return 'neutral';
}

function getTweakStatusLabel(stateText: string): string {
  if (stateText === 'PartiallyOptimized') return 'User Only (HKCU)';
  return stateText;
}

export function OptimizeView({ onNotify }: OptimizeViewProps) {
  const [state, setState] = useState<OptimizerStateDto | null>(null);
  const [draftProfile, setDraftProfile] = useState<OptimizerProfileDto | null>(
    null
  );
  const [activeTab, setActiveTab] = useState<OptimizerTab>('cacheNetwork');
  const [expandedIds, setExpandedIds] = useState<Record<string, boolean>>({});
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [confirmAction, setConfirmAction] = useState<
    null | 'restartExplorer' | 'restoreDefaults'
  >(null);

  const iconCacheSelectId = useId();

  const loadState = useCallback(async () => {
    try {
      const next = await bridge.optimizer.getState();
      setState(next);
      setDraftProfile(next.profile);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to inspect Windows optimization state.'
      );
    }
  }, [onNotify]);

  useEffect(() => {
    void loadState();
  }, [loadState]);

  const toggleDisclosure = (id: string) => {
    setExpandedIds((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  const setProfileField = <K extends keyof OptimizerProfileDto>(
    key: K,
    value: OptimizerProfileDto[K]
  ) => {
    setDraftProfile((prev) => (prev ? { ...prev, [key]: value } : prev));
  };

  const handleLoadRecommended = async () => {
    setBusyAction('recommended');
    try {
      const rec = await bridge.optimizer.getRecommended();
      setDraftProfile(rec);
      onNotify(
        'info',
        'Recommended optimization profile loaded. Click Apply Optimizations to save.'
      );
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error
          ? err.message
          : 'Failed to load recommended profile.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const handleApply = async (elevateViaUac: boolean) => {
    if (!draftProfile) return;
    setBusyAction(elevateViaUac ? 'elevateApply' : 'apply');
    try {
      const res = await bridge.optimizer.apply(draftProfile, elevateViaUac);
      if (res.state) {
        setState(res.state);
        setDraftProfile(res.state.profile);
      }
      onNotify(
        res.ok
          ? res.requiresElevationForFullCoverage
            ? 'warning'
            : 'info'
          : res.uacCancelled
            ? 'warning'
            : 'error',
        res.message
      );
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Failed to apply optimizations.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const runShellTool = async (
    actionKey: string,
    fn: () => Promise<{ ok: boolean; message: string; state: OptimizerStateDto | null }>
  ) => {
    setBusyAction(actionKey);
    try {
      const res = await fn();
      if (res.state) {
        setState(res.state);
        setDraftProfile(res.state.profile);
      }
      onNotify(res.ok ? 'info' : 'error', res.message);
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Shell action failed.'
      );
    } finally {
      setBusyAction(null);
    }
  };

  const needsElevationForDraft = useMemo(() => {
    if (!draftProfile || state?.isElevated) return false;
    return (
      draftProfile.preventThumbCacheCleanup ||
      draftProfile.clearDiskCleanupFlags ||
      draftProfile.disableSilentCleanupTask ||
      draftProfile.expandIconCache
    );
  }, [draftProfile, state?.isElevated]);

  const tabTweaks = useMemo(() => {
    return (state?.tweaks ?? []).filter((t) => t.tab === activeTab);
  }, [activeTab, state?.tweaks]);

  const startupTweak = useMemo(
    () => (state?.tweaks ?? []).find((t) => t.id === 'enforceOnStartup'),
    [state?.tweaks]
  );

  const renderDisclosure = (
    id: string,
    whatItDoes: string,
    pros: string,
    cons: string
  ) => {
    const isExpanded = Boolean(expandedIds[id]);
    const panelId = `opt-disclosure-${id}`;
    return (
      <div>
        <Button
          variant="ghost"
          size="sm"
          aria-expanded={isExpanded}
          aria-controls={panelId}
          onClick={() => toggleDisclosure(id)}
        >
          {isExpanded ? (
            <ChevronDown size={14} strokeWidth={2} aria-hidden="true" />
          ) : (
            <ChevronRight size={14} strokeWidth={2} aria-hidden="true" />
          )}
          <span>Details &amp; Trade-offs</span>
        </Button>
        {isExpanded && (
          <div id={panelId} className="ds-disclosure-panel" style={{ marginTop: 8 }}>
            <div>
              <strong>What it does:</strong> {whatItDoes}
            </div>
            <div>
              <strong>Pros:</strong> {pros}
            </div>
            <div>
              <strong>Cons / Notes:</strong> {cons}
            </div>
          </div>
        )}
      </div>
    );
  };

  const renderTweakCard = (tweak: OptimizerTweakDto) => {
    const profileKey = PROFILE_KEYS[tweak.id];
    const checked =
      draftProfile && profileKey ? Boolean(draftProfile[profileKey]) : tweak.enabled;

    return (
      <article key={tweak.id} className="ds-card ds-stack-sm">
        <div className="ds-row-between">
          <div className="ds-row">
            <StatusPill tone={getTweakStatusTone(tweak.state)}>
              {getTweakStatusLabel(tweak.state)}
            </StatusPill>
            {tweak.requiresAdmin && (
              <StatusPill
                tone="neutral"
                icon={<Shield size={12} strokeWidth={2} aria-hidden="true" />}
              >
                Requires Admin (HKLM)
              </StatusPill>
            )}
          </div>
        </div>

        <Switch
          label={tweak.title}
          checked={checked}
          disabled={!draftProfile}
          onChange={(nextVal) => {
            if (profileKey) {
              setProfileField(profileKey, nextVal);
            }
          }}
        />

        {renderDisclosure(tweak.id, tweak.whatItDoes, tweak.pros, tweak.cons)}
      </article>
    );
  };

  const shellToolMap = useMemo(() => {
    const map: Record<string, (typeof state extends null ? never : NonNullable<typeof state>['shellTools'][number])> = {};
    for (const tool of state?.shellTools ?? []) {
      map[tool.id] = tool;
    }
    return map;
  }, [state?.shellTools]);

  return (
    <div className="ds-stack-md">
      <div className="ds-page-header">
        <div>
          <h1 id="main-heading" tabIndex={-1} className="ds-title">
            Windows Optimizer
          </h1>
          <p className="ds-subtitle">
            Prevent Windows maintenance from resetting your thumbnail cache and
            tune File Explorer performance policies.
          </p>
        </div>
        <div className="ds-row">
          <StatusPill
            tone={state?.isElevated ? 'active' : 'neutral'}
            icon={<Shield size={13} strokeWidth={2} aria-hidden="true" />}
          >
            {state?.isElevated ? 'Admin' : 'Standard User'}
          </StatusPill>
          <Button
            variant="secondary"
            size="sm"
            loading={busyAction === 'recommended'}
            onClick={() => void handleLoadRecommended()}
          >
            <Sparkles size={14} strokeWidth={2} aria-hidden="true" />
            <span>Load Recommended</span>
          </Button>
        </div>
      </div>

      <GlassPanel>
        <Switch
          label="Re-apply on App Startup"
          hint={
            startupTweak?.whatItDoes ??
            'Automatically re-apply saved Windows optimizations whenever WinThumbsPreloader launches.'
          }
          checked={draftProfile?.enforceOnStartup ?? true}
          disabled={!draftProfile}
          onChange={(val) => setProfileField('enforceOnStartup', val)}
        />
      </GlassPanel>

      <div className="ds-row-between">
        <SegmentedControl
          mode="tablist"
          idPrefix="opt-tabs"
          ariaLabel="Optimizer categories"
          value={activeTab}
          onChange={setActiveTab}
          options={[
            { value: 'cacheNetwork', label: 'Cache & Network' },
            { value: 'explorer', label: 'Explorer Performance' },
            { value: 'shell', label: 'Shell & Maintenance' },
          ]}
        />
      </div>

      <div
        role="tabpanel"
        id={`opt-tabs-panel-${activeTab}`}
        aria-labelledby={`opt-tabs-tab-${activeTab}`}
      >
        {activeTab !== 'shell' ? (
        <GlassPanel
          title={
            activeTab === 'cacheNetwork'
              ? 'Cache Protection & Network Folders'
              : 'Explorer Performance & Icon Cache'
          }
        >
          <div className="ds-stack-md">
            {tabTweaks.map(renderTweakCard)}

            {activeTab === 'explorer' && (
              <div className="ds-card ds-row-between">
                <div className="ds-field" style={{ flex: '1 1 260px' }}>
                  <label htmlFor={iconCacheSelectId} className="ds-label">
                    Icon Cache Size (Current:{' '}
                    {state?.currentIconCacheSizeKb ?? 500} KB)
                  </label>
                  <select
                    id={iconCacheSelectId}
                    className="ds-select"
                    disabled={!draftProfile?.expandIconCache}
                    value={draftProfile?.iconCacheSizeKb ?? 8192}
                    onChange={(e) =>
                      setProfileField('iconCacheSizeKb', Number(e.target.value))
                    }
                  >
                    <option value={4096}>4096 KB (4 MB)</option>
                    <option value={8192}>8192 KB (8 MB)</option>
                    <option value={16384}>16384 KB (16 MB)</option>
                  </select>
                </div>
              </div>
            )}
          </div>
        </GlassPanel>
      ) : (
        <GlassPanel title="Shell Integration &amp; Maintenance Tools">
          <div className="ds-stack-md">
            <article className="ds-card ds-stack-sm">
              <div className="ds-row-between">
                <div className="ds-row">
                  <FolderCog size={18} strokeWidth={2} aria-hidden="true" />
                  <span className="ds-resource-name">
                    Explorer Context Menu
                  </span>
                </div>
                <StatusPill
                  tone={state?.contextMenuRegistered ? 'active' : 'neutral'}
                >
                  {state?.contextMenuRegistered
                    ? state.contextMenuSystemWide
                      ? 'Registered (System-Wide)'
                      : 'Registered (Current User)'
                    : 'Not Registered'}
                </StatusPill>
              </div>
              <div className="ds-row">
                <Button
                  variant="secondary"
                  size="sm"
                  loading={busyAction === 'registerMenu'}
                  disabled={busyAction !== null}
                  onClick={() =>
                    void runShellTool('registerMenu', () =>
                      bridge.optimizer.registerContextMenu()
                    )
                  }
                >
                  Register Context Menu
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  loading={busyAction === 'unregisterMenu'}
                  disabled={busyAction !== null || !state?.contextMenuRegistered}
                  onClick={() =>
                    void runShellTool('unregisterMenu', () =>
                      bridge.optimizer.unregisterContextMenu()
                    )
                  }
                >
                  Remove Context Menu
                </Button>
              </div>
              {shellToolMap.contextMenu &&
                renderDisclosure(
                  'tool-contextMenu',
                  shellToolMap.contextMenu.whatItDoes,
                  shellToolMap.contextMenu.pros,
                  shellToolMap.contextMenu.cons
                )}
            </article>

            <article className="ds-card ds-stack-sm">
              <div className="ds-row-between">
                <div className="ds-row">
                  <Wrench size={18} strokeWidth={2} aria-hidden="true" />
                  <span className="ds-resource-name">
                    Icon Cache &amp; Explorer Shell
                  </span>
                </div>
              </div>
              <div className="ds-row">
                <Button
                  variant="secondary"
                  size="sm"
                  loading={busyAction === 'rebuildIconCache'}
                  disabled={busyAction !== null}
                  onClick={() =>
                    void runShellTool('rebuildIconCache', () =>
                      bridge.optimizer.rebuildIconCache()
                    )
                  }
                >
                  <RefreshCw size={14} strokeWidth={2} aria-hidden="true" />
                  <span>Rebuild Icon Cache Only</span>
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  loading={busyAction === 'restartExplorer'}
                  disabled={busyAction !== null}
                  onClick={() => setConfirmAction('restartExplorer')}
                >
                  Restart Windows Explorer
                </Button>
                <Button
                  variant="danger"
                  size="sm"
                  loading={busyAction === 'restoreDefaults'}
                  disabled={busyAction !== null}
                  onClick={() => setConfirmAction('restoreDefaults')}
                >
                  <RotateCcw size={14} strokeWidth={2} aria-hidden="true" />
                  <span>Restore Windows Defaults</span>
                </Button>
              </div>
              {shellToolMap.rebuildIconCache &&
                renderDisclosure(
                  'tool-rebuildIconCache',
                  shellToolMap.rebuildIconCache.whatItDoes,
                  shellToolMap.rebuildIconCache.pros,
                  shellToolMap.rebuildIconCache.cons
                )}
              {shellToolMap.restartExplorer &&
                renderDisclosure(
                  'tool-restartExplorer',
                  shellToolMap.restartExplorer.whatItDoes,
                  shellToolMap.restartExplorer.pros,
                  shellToolMap.restartExplorer.cons
                )}
              {shellToolMap.restoreDefaults &&
                renderDisclosure(
                  'tool-restoreDefaults',
                  shellToolMap.restoreDefaults.whatItDoes,
                  shellToolMap.restoreDefaults.pros,
                  shellToolMap.restoreDefaults.cons
                )}
            </article>
          </div>
        </GlassPanel>
      )}
      </div>

      <div className="ds-row-between">
        <Button
          variant="secondary"
          loading={busyAction === 'recommended'}
          disabled={busyAction !== null}
          onClick={() => void handleLoadRecommended()}
        >
          <Sparkles size={14} strokeWidth={2} aria-hidden="true" />
          <span>Load Recommended</span>
        </Button>

        <div className="ds-row">
          {!state?.isElevated && needsElevationForDraft && (
            <Button
              variant="secondary"
              loading={busyAction === 'elevateApply'}
              disabled={!draftProfile || busyAction !== null}
              onClick={() => void handleApply(true)}
            >
              <Shield size={14} strokeWidth={2} aria-hidden="true" />
              <span>Elevate &amp; Apply (UAC)</span>
            </Button>
          )}
          <Button
            variant="primary"
            loading={busyAction === 'apply'}
            loadingLabel="Applying..."
            disabled={!draftProfile || busyAction !== null}
            onClick={() => void handleApply(false)}
          >
            <span>Apply Optimizations</span>
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirmAction === 'restartExplorer'}
        title="Restart Windows Explorer?"
        description="Closes open File Explorer windows and relaunches explorer.exe so ShellBag, Folder Type Discovery, and Icon Cache updates take effect immediately."
        confirmLabel="Restart Explorer"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runShellTool('restartExplorer', () =>
            bridge.optimizer.restartExplorer()
          );
        }}
        onCancel={() => setConfirmAction(null)}
      />

      <ConfirmDialog
        open={confirmAction === 'restoreDefaults'}
        title="Restore Windows default settings?"
        description="Removes all registry policies and restores stock Windows 10/11 defaults (including Automatic Maintenance thumbnail cache cleanup)."
        confirmLabel="Restore Windows Defaults"
        destructive
        onConfirm={() => {
          setConfirmAction(null);
          void runShellTool('restoreDefaults', () =>
            bridge.optimizer.restoreDefaults()
          );
        }}
        onCancel={() => setConfirmAction(null)}
      />
    </div>
  );
}
