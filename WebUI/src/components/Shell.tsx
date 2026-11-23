import { useState, type ReactNode } from 'react';
import {
  CalendarClock,
  Database,
  Download,
  FileImage,
  FolderUp,
  Layers,
  PanelBottomClose,
  Power,
  Settings,
  Shield,
  Sliders,
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

interface NavItem {
  id: ViewId;
  label: string;
  icon: typeof FolderUp;
}

const NAV_ITEMS: ReadonlyArray<NavItem> = [
  { id: 'preload', label: 'Preload', icon: FolderUp },
  { id: 'cache', label: 'Cache', icon: Database },
  { id: 'schedule', label: 'Schedule', icon: CalendarClock },
  { id: 'codecs', label: 'Codecs', icon: FileImage },
  { id: 'optimizer', label: 'Optimize', icon: Sliders },
  { id: 'settings', label: 'Settings', icon: Settings },
];

export interface ShellProps {
  activeView: ViewId;
  onNavigate: (view: ViewId) => void;
  appName: string;
  version: string;
  isAdmin: boolean;
  cacheSnapshot: CacheSnapshot | null;
  cacheProgress: CacheProgressEvent | null;
  activeCodecsCount: number;
  totalCodecsCount: number;
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
  cacheSnapshot,
  cacheProgress,
  activeCodecsCount,
  totalCodecsCount,
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

  return (
    <div className="ds-shell">
      <a href="#main-heading" className="ds-skip-link visually-hidden">
        Skip to main content
      </a>

      <header className="ds-header ds-glass">
        <div className="ds-brand">
          <Layers size={24} strokeWidth={2} aria-hidden="true" />
          <span className="ds-brand-title">{appName}</span>
        </div>

        <nav aria-label="Primary" className="ds-nav">
          {NAV_ITEMS.map((item) => {
            const Icon = item.icon;
            const current = activeView === item.id;
            return (
              <button
                key={item.id}
                type="button"
                className="ds-nav-link"
                aria-current={current ? 'page' : undefined}
                onClick={() => onNavigate(item.id)}
              >
                <Icon size={16} strokeWidth={2} aria-hidden="true" />
                <span>{item.label}</span>
              </button>
            );
          })}
        </nav>

        <div className="ds-header-actions">
          {updateStatus?.isUpdateAvailable && (
            <Button
              variant="secondary"
              size="sm"
              onClick={onOpenUpdateModal}
            >
              <Download size={15} strokeWidth={2} aria-hidden="true" />
              <span>Update Available</span>
            </Button>
          )}
          <Button
            variant="ghost"
            size="sm"
            aria-label="Minimize to Tray"
            title="Minimize to Tray"
            onClick={onMinimizeToTray}
          >
            <PanelBottomClose size={15} strokeWidth={2} aria-hidden="true" />
            <span>Tray</span>
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={handleQuitClick}
          >
            <Power size={15} strokeWidth={2} aria-hidden="true" />
            <span>Quit</span>
          </Button>
        </div>
      </header>

      <main className="ds-main ds-scroller">
        <div className="ds-content">{children}</div>
      </main>

      <footer className="ds-footer">
        <div className="ds-footer-group">
          <span className="ds-footer-metric">
            <span>Explorer Cache</span>
            <span className="ds-footer-value">
              {cacheSnapshot?.explorerSizeFormatted ?? '—'}
            </span>
          </span>
          <span className="ds-footer-metric">
            <span>Backup</span>
            <span className="ds-footer-value">
              {cacheSnapshot?.backupSizeFormatted ?? '—'}
            </span>
          </span>
          <span className="ds-footer-metric">
            <span>Codecs</span>
            <span className="ds-footer-value">
              {totalCodecsCount > 0
                ? `${activeCodecsCount}/${totalCodecsCount}`
                : '—'}
            </span>
          </span>
          {cacheProgress !== null && (
            <span className="ds-footer-metric">
              <progress
                className="ds-progress ds-progress-compact"
                value={cacheProgress.percent}
                max={100}
                aria-label={`${cacheProgress.operation} progress`}
              />
              <span className="ds-footer-value">
                {cacheProgress.operation} {cacheProgress.percent}%
              </span>
            </span>
          )}
        </div>

        <div className="ds-footer-group">
          <StatusPill
            tone={isAdmin ? 'warning' : 'neutral'}
            icon={<Shield size={12} strokeWidth={2} aria-hidden="true" />}
          >
            {isAdmin ? 'Admin' : 'Standard'}
          </StatusPill>
          <span className="ds-footer-value">v{version}</span>
        </div>
      </footer>

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
