import { useEffect, useState } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  Download,
  ExternalLink,
  RefreshCw,
} from 'lucide-react';
import { bridge } from '../lib/bridge';
import type { UpdateProgressEvent, UpdateStatusDto } from '../lib/types';
import { Button } from './ui/Button';
import { ModalDialog } from './ui/ModalDialog';
import { StatusPill } from './ui/StatusPill';

export interface UpdateModalProps {
  open: boolean;
  currentVersion: string;
  updateStatus: UpdateStatusDto | null;
  progress: UpdateProgressEvent | null;
  onStatusUpdated: (next: UpdateStatusDto) => void;
  onClose: () => void;
}

function formatInstallerSize(bytes: number): string {
  if (!bytes || bytes <= 0) return '';
  const mb = bytes / (1024 * 1024);
  return `${mb.toFixed(1)} MB`;
}

export function UpdateModal({
  open,
  currentVersion,
  updateStatus,
  progress,
  onStatusUpdated,
  onClose,
}: UpdateModalProps) {
  const [checking, setChecking] = useState(false);
  const [installing, setInstalling] = useState(false);
  const [feedback, setFeedback] = useState<{
    tone: 'info' | 'error';
    message: string;
  } | null>(null);

  useEffect(() => {
    if (open) {
      setFeedback(null);
    }
  }, [open]);

  const releaseUrl =
    updateStatus?.releasePageUrl ||
    'https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases';

  const handleCheckAgain = async () => {
    setChecking(true);
    setFeedback(null);
    try {
      const next = await bridge.checkForUpdates();
      onStatusUpdated(next);
      if (!next.isUpdateAvailable) {
        setFeedback({
          tone: next.state === 'Error' ? 'error' : 'info',
          message:
            next.state === 'Error'
              ? 'Could not reach GitHub releases. Check your internet connection.'
              : `WinThumbsPreloader v${next.currentVersion || currentVersion} is up to date.`,
        });
      }
    } catch (err) {
      setFeedback({
        tone: 'error',
        message: err instanceof Error ? err.message : 'Update check failed.',
      });
    } finally {
      setChecking(false);
    }
  };

  const handleInstall = async () => {
    setInstalling(true);
    setFeedback(null);
    try {
      const res = await bridge.downloadAndInstallUpdate(false);
      if (!res.ok) {
        setFeedback({
          tone: res.openedReleasePageFallback ? 'info' : 'error',
          message: res.message,
        });
      }
    } catch (err) {
      setFeedback({
        tone: 'error',
        message:
          err instanceof Error ? err.message : 'Failed to install update.',
      });
    } finally {
      setInstalling(false);
    }
  };

  const handleOpenGitHub = async () => {
    try {
      await bridge.openExternalUrl(releaseUrl);
    } catch (err) {
      setFeedback({
        tone: 'error',
        message:
          err instanceof Error ? err.message : 'Failed to open release page.',
      });
    }
  };

  return (
    <ModalDialog
      open={open}
      title="Application Update"
      description="Check for new WinThumbsPreloader releases or install verified updates from GitHub."
      onClose={onClose}
      lightDismiss={!installing}
      allowClose={!installing}
      icon={<Download size={20} strokeWidth={2} aria-hidden="true" />}
      actions={
        <>
          <Button
            variant="ghost"
            onClick={handleCheckAgain}
            loading={checking}
            disabled={installing}
          >
            <RefreshCw size={14} strokeWidth={2} aria-hidden="true" />
            <span>Check Again</span>
          </Button>
          <Button
            variant="secondary"
            onClick={handleOpenGitHub}
            disabled={installing}
          >
            <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
            <span>View Release on GitHub</span>
          </Button>
          {updateStatus?.isUpdateAvailable && (
            <Button
              variant="primary"
              onClick={handleInstall}
              loading={installing}
              loadingLabel="Downloading..."
            >
              <Download size={14} strokeWidth={2} aria-hidden="true" />
              <span>Download &amp; Install</span>
            </Button>
          )}
          <Button variant="secondary" onClick={onClose} disabled={installing}>
            Dismiss
          </Button>
        </>
      }
    >
      <div className="ds-stack-md">
        <div className="ds-card ds-stack-sm">
          <div className="ds-row-between">
            <span className="ds-label">Installed Version</span>
            <span className="ds-mono">
              v{updateStatus?.currentVersion || currentVersion}
            </span>
          </div>
          <div className="ds-row-between">
            <span className="ds-label">Latest Release</span>
            {updateStatus?.isUpdateAvailable ? (
              <StatusPill tone="active">
                v{updateStatus.latestVersion}
                {updateStatus.state === 'BetaAvailable' ? ' (Beta)' : ''}
              </StatusPill>
            ) : (
              <StatusPill
                tone={updateStatus?.state === 'Error' ? 'error' : 'neutral'}
              >
                {updateStatus?.latestVersion
                  ? `v${updateStatus.latestVersion}`
                  : 'Checked'}
              </StatusPill>
            )}
          </div>
          {updateStatus?.hasInstallerAsset && (
            <div className="ds-row-between">
              <span className="ds-label">Installer Asset</span>
              <span className="ds-hint ds-ellipsis">
                {updateStatus.installerFileName}{' '}
                {updateStatus.installerSize > 0
                  ? `(${formatInstallerSize(updateStatus.installerSize)})`
                  : ''}
              </span>
            </div>
          )}
        </div>

        {(checking || installing || progress) && (
          <div className="ds-stack-sm">
            <label htmlFor="update-progress-bar" className="ds-label">
              {progress?.status ||
                (checking ? 'Checking GitHub releases...' : 'Downloading update...')}
            </label>
            <progress
              id="update-progress-bar"
              className="ds-progress"
              value={progress ? progress.percent : undefined}
              max={100}
            />
          </div>
        )}

        {feedback && (
          <div
            role={feedback.tone === 'error' ? 'alert' : 'status'}
            className={
              feedback.tone === 'error' ? 'ds-alert' : 'ds-card ds-row'
            }
          >
            {feedback.tone === 'error' ? (
              <AlertCircle size={16} strokeWidth={2} aria-hidden="true" />
            ) : (
              <CheckCircle2 size={16} strokeWidth={2} aria-hidden="true" />
            )}
            <span>{feedback.message}</span>
          </div>
        )}
      </div>
    </ModalDialog>
  );
}
