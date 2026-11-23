import { useState, type ReactNode } from 'react';
import {
  CalendarClock,
  Database,
  Download,
  FileImage,
  FolderUp,
  Layers,
  LayoutGrid,
  Moon,
  PanelBottomClose,
  Power,
  Settings,
  Shield,
  Sliders,
  Sun,
} from 'lucide-react';
import type {
  CacheProgressEvent,
  CacheSnapshot,
  UpdateStatusDto,
  ViewId,
} from '../lib/types';
import { Button } from './ui/Button';
import { ConfirmDialog } from './ui/ModalDialog';
import { StatusPill } from './ui/StatusPill';

type NavGroupId = 'WORKSPACE' | 'SYSTEM' | 'PREFERENCES';

interface NavItem {
  id: ViewId;
  label: string;
  group: NavGroupId;
  icon: typeof FolderUp;
}

const NAV_ITEMS: ReadonlyArray<NavItem> = [
  { id: 'overview', label: 'Overview', group: 'WORKSPACE', icon: LayoutGrid },
  { id: 'preload', label: 'Preload', group: 'WORKSPACE', icon: FolderUp },
  { id: 'cache', label: 'Cache', group: 'WORKSPACE', icon: Database },
  { id: 'schedule', label: 'Schedule', group: 'WORKSPACE', icon: CalendarClock },
  { id: 'codecs', label: 'Codecs', group: 'SYSTEM', icon: FileImage },
  { id: 'optimizer', label: 'Optimize', group: 'SYSTEM', icon: Sliders },
  { id: 'settings', label: 'Settings', group: 'PREFERENCES', icon: Settings },
];

const NAV_GROUPS: ReadonlyArray<NavGroupId> = [
  'WORKSPACE',
  'SYSTEM',
  'PREFERENCES',
];

export interface ShellProps {
  activeView: ViewId;
  onNavigate: (view: ViewId) => void;
  appName: string;
  version: string;
  isAdmin: boolean;
  effectiveTheme: 'dark' | 'light';
  onToggleTheme: () => void;
  cacheSnapshot: CacheSnapshot | null;
  cacheProgress: CacheProgressEvent | null;
  stagedQueueCount: number;
  activeCodecsCount: number;
  totalCodecsCount: number;
  scheduleEnabled: boolean | null;
  appliedOptimizationsCount: number | null;
  totalOptimizationsCount: number | null;
  updateStatus: UpdateStatusDto | null;
  onOpenUpdateModal: () => void;
  onMinimizeToTray: () => void;
  onQuit: () => void;
  children: ReactNode;
}

export function Shell({
  activeView,
  onNavigate,
  appName,
  version,
  isAdmin,
  effectiveTheme,
  onToggleTheme,
  cacheSnapshot,
  cacheProgress,
  stagedQueueCount,
  activeCodecsCount,
  totalCodecsCount,
  scheduleEnabled,
  appliedOptimizationsCount,
  totalOptimizationsCount,
  updateStatus,
  onOpenUpdateModal,
  onMinimizeToTray,
  onQuit,
  children,
}: ShellProps) {
  const [confirmQuitOpen, setConfirmQuitOpen] = useState(false);

  const handleQuitClick = () => {
    if (cacheProgress !== null) {
      setConfirmQuitOpen(true);
    } else {
      onQuit();
    }
  };

  const getBadgeText = (id: ViewId): string | null => {
    switch (id) {
      case 'preload':
        return stagedQueueCount > 0 ? String(stagedQueueCount) : null;
      case 'cache':
        return cacheSnapshot?.explorerSizeFormatted ?? null;
      case 'schedule':
        return scheduleEnabled === null ? null : scheduleEnabled ? 'ON' : 'OFF';
      case 'codecs':
        return totalCodecsCount > 0
          ? `${activeCodecsCount}/${totalCodecsCount}`
          : null;
      case 'optimizer':
        return appliedOptimizationsCount !== null && totalOptimizationsCount
          ? `${appliedOptimizationsCount}/${totalOptimizationsCount}`
          : null;
      case 'settings':
        return updateStatus?.isUpdateAvailable ? 'Update' : null;
      default:
        return null;
    }
  };

  const activeBucketsCount = (cacheSnapshot?.buckets ?? []).filter(
    (b) => b.sizeBytes > 0
  ).length;
  const healthWarning = Boolean(cacheSnapshot?.resetRecognized);

  return (
    <div className="ds-shell">
      <a href="#main-heading" className="ds-skip-link visually-hidden">
        Skip to main content
      </a>

      <aside className="ds-sidebar" aria-label="Workspace sidebar">
        <div className="ds-sidebar-top">
          <div className="ds-sidebar-brand">
            <div className="ds-brand">
              <Layers size={20} strokeWidth={2} aria-hidden="true" />
              <span className="ds-brand-title">{appName}</span>
            </div>
            <span className="ds-version-tag">v{version}</span>
          </div>

          <nav aria-label="Primary" className="ds-nav">
            {NAV_GROUPS.map((group) => (
              <div key={group} className="ds-nav-group">
                <span className="ds-nav-group-label">{group}</span>
                {NAV_ITEMS.filter((item) => item.group === group).map((item) => {
                  const Icon = item.icon;
                  const current = activeView === item.id;
                  const badge = getBadgeText(item.id);
                  return (
                    <button
                      key={item.id}
                      type="button"
                      className="ds-nav-link"
                      aria-current={current ? 'page' : undefined}
                      onClick={() => onNavigate(item.id)}
                    >
                      <span className="ds-nav-link-lead">
                        <Icon size={15} strokeWidth={2} aria-hidden="true" />
                        <span>{item.label}</span>
                      </span>
                      {badge && (
                        <span className="ds-nav-badge" aria-hidden="true">
                          {badge}
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>
            ))}
          </nav>
        </div>

        <div className="ds-sidebar-footer">
          <button
            type="button"
            className="ds-sidebar-health"
            data-warning={healthWarning}
            aria-label="Open Cache Manager"
            onClick={() => onNavigate('cache')}
          >
            <div className="ds-sidebar-health-head">
              <span className="ds-sidebar-health-label">Cache Health</span>
              <span className="ds-sidebar-health-state">
                <span
                  className={
                    healthWarning ? 'ds-status-dot-warn' : 'ds-status-dot-ok'
                  }
                  aria-hidden="true"
                />
                <span>
                  {cacheProgress
                    ? `${cacheProgress.percent}%`
                    : healthWarning
                      ? 'Reset Alert'
                      : 'Synced'}
                </span>
              </span>
            </div>
            <span className="ds-sidebar-health-metric">
              {cacheSnapshot
                ? `${cacheSnapshot.explorerSizeFormatted} · ${activeBucketsCount} Buckets`
                : 'Loading cache...'}
            </span>
            <progress
              className="ds-progress ds-progress-compact ds-progress-ok"
              value={cacheProgress ? cacheProgress.percent : 100}
              max={100}
              aria-label="Cache health progress"
            />
          </button>

          {updateStatus?.isUpdateAvailable && (
            <Button
              variant="secondary"
              size="sm"
              onClick={onOpenUpdateModal}
            >
              <Download size={14} strokeWidth={2} aria-hidden="true" />
              <span>Update Available</span>
            </Button>
          )}

          <div className="ds-sidebar-utils">
            <StatusPill
              tone={isAdmin ? 'warning' : 'neutral'}
              icon={<Shield size={12} strokeWidth={2} aria-hidden="true" />}
            >
              {isAdmin ? 'Admin' : 'Standard'}
            </StatusPill>

            <div className="ds-sidebar-util-icons">
              <Button
                variant="ghost"
                size="icon"
                aria-label={
                  effectiveTheme === 'dark'
                    ? 'Switch to Light Theme'
                    : 'Switch to Dark Theme'
                }
                title={
                  effectiveTheme === 'dark'
                    ? 'Switch to Light Theme'
                    : 'Switch to Dark Theme'
                }
                onClick={onToggleTheme}
              >
                {effectiveTheme === 'dark' ? (
                  <Sun size={15} strokeWidth={2} aria-hidden="true" />
                ) : (
                  <Moon size={15} strokeWidth={2} aria-hidden="true" />
                )}
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label="Minimize to Tray"
                title="Minimize to Tray"
                onClick={onMinimizeToTray}
              >
                <PanelBottomClose size={15} strokeWidth={2} aria-hidden="true" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label="Quit"
                title="Quit"
                onClick={handleQuitClick}
              >
                <Power size={15} strokeWidth={2} aria-hidden="true" />
              </Button>
            </div>
          </div>
        </div>
      </aside>

      <main className="ds-main ds-scroller">
        <div className="ds-content">{children}</div>
      </main>

      <ConfirmDialog
        open={confirmQuitOpen}
        title="Cache operation in progress"
        description="A thumbnail cache backup or restore is still running. Quitting now may leave an incomplete operation."
        confirmLabel="Quit Anyway"
        destructive
        onConfirm={() => {
          setConfirmQuitOpen(false);
          onQuit();
        }}
        onCancel={() => setConfirmQuitOpen(false)}
      />
    </div>
  );
}
