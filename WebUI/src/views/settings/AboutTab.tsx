import { useState } from 'react';
import {
  Copy,
  Download,
  ExternalLink,
  Layers,
  RefreshCw,
  Shield,
} from 'lucide-react';
import { bridge } from '../../lib/bridge';
import type { AboutInfoDto, UpdateStatusDto } from '../../lib/types';
import { Button } from '../../components/ui/Button';
import { GlassPanel } from '../../components/ui/GlassPanel';
import { StatusPill } from '../../components/ui/StatusPill';

export interface AboutTabProps {
  about: AboutInfoDto;
  updateStatus: UpdateStatusDto | null;
  onUpdateStatusChanged: (next: UpdateStatusDto) => void;
  onOpenUpdateModal: () => void;
  onNotify: (tone: 'info' | 'warning' | 'error', message: string) => void;
}

const CLI_REFERENCE: ReadonlyArray<{
  flag: string;
  description: string;
  example: string;
}> = [
  {
    flag: '-r',
    description: 'Include nested subdirectories recursively.',
    example: 'WinThumbsPreloader.exe -r "D:\\Photos"',
  },
  {
    flag: '-s',
    description: 'Silent mode (suppresses progress dialog UI).',
    example: 'WinThumbsPreloader.exe -r -s "D:\\Photos"',
  },
  {
    flag: '-m',
    description: 'Enable multi-threaded preloading (auto thread count).',
    example: 'WinThumbsPreloader.exe -r -m "D:\\Photos"',
  },
  {
    flag: '-m:<n>',
    description: 'Enable multi-threaded preloading with explicit thread count.',
    example: 'WinThumbsPreloader.exe -r -m:8 "D:\\Photos"',
  },
  {
    flag: '-maxinstances:<n>',
    description: 'Limit concurrent WinThumbsPreloader instances (1 = sequential).',
    example: 'WinThumbsPreloader.exe -r -maxinstances:1 "D:\\Photos" "E:\\Video"',
  },
  {
    flag: '-startminimized',
    description: 'Start minimized in the Windows notification tray.',
    example: 'WinThumbsPreloader.exe -startminimized',
  },
  {
    flag: '-reopensettings',
    description: 'Open or focus the shell directly on the Settings view.',
    example: 'WinThumbsPreloader.exe -reopensettings',
  },
  {
    flag: '-reopenoptimizer',
    description: 'Open or focus the shell directly on Windows Optimizer.',
    example: 'WinThumbsPreloader.exe -reopenoptimizer',
  },
  {
    flag: '-reopencodecs',
    description: 'Open or focus the shell directly on Thumbnail Codecs.',
    example: 'WinThumbsPreloader.exe -reopencodecs',
  },
  {
    flag: '-apply-optimizations',
    description: 'Apply saved or masked Windows optimizations headlessly.',
    example: 'WinThumbsPreloader.exe -apply-optimizations -profile-mask:0x01FF -icon-cache-kb:8192',
  },
  {
    flag: '-restore-defaults',
    description: 'Restore default Windows thumbnail and Explorer registry policies.',
    example: 'WinThumbsPreloader.exe -restore-defaults',
  },
  {
    flag: '-apply-codecs -codecs-mask:0x...',
    description: 'Register bundled codec families matching the hex bitmask.',
    example: 'WinThumbsPreloader.exe -apply-codecs -codecs-mask:0x3FF',
  },
  {
    flag: '-uninstall-codecs',
    description: 'Uninstall built-in codecs and restore backed-up handlers.',
    example: 'WinThumbsPreloader.exe -uninstall-codecs',
  },
  {
    flag: '-shutdown',
    description: 'Signal any running GUI instance to exit cleanly.',
    example: 'WinThumbsPreloader.exe -shutdown',
  },
];

export function AboutTab({
  about,
  updateStatus,
  onUpdateStatusChanged,
  onOpenUpdateModal,
  onNotify,
}: AboutTabProps) {
  const [checking, setChecking] = useState(false);

  const handleCheckUpdates = async () => {
    setChecking(true);
    try {
      const next = await bridge.checkForUpdates();
      onUpdateStatusChanged(next);
      if (next.isUpdateAvailable) {
        onOpenUpdateModal();
      } else {
        onNotify(
          next.state === 'Error' ? 'error' : 'info',
          next.state === 'Error'
            ? 'Failed to check for updates.'
            : `WinThumbsPreloader v${next.currentVersion || about.version} is up to date.`
        );
      }
    } catch (err) {
      onNotify(
        'error',
        err instanceof Error ? err.message : 'Update check failed.'
      );
    } finally {
      setChecking(false);
    }
  };

  const handleCopyExample = async (example: string) => {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(example);
        onNotify('info', 'CLI example copied to clipboard.');
      }
    } catch {
      onNotify('warning', 'Clipboard access is unavailable.');
    }
  };

  return (
    <div className="ds-stack-md">
      <div className="ds-grid-2">
        <GlassPanel title="Application">
          <div className="ds-row-between">
            <div className="ds-row">
              <Layers size={24} strokeWidth={2} aria-hidden="true" />
              <div className="ds-stack-sm">
                <span className="ds-resource-name">
                  {about.appName} v{about.version}
                </span>
                <span className="ds-hint">
                  Pre-populate and safeguard Windows Explorer thumbnail caches.
                </span>
              </div>
            </div>
            <StatusPill
              tone={about.isRunningAsAdmin ? 'warning' : 'neutral'}
              icon={<Shield size={12} strokeWidth={2} aria-hidden="true" />}
            >
              {about.isRunningAsAdmin ? 'Admin' : 'Standard'}
            </StatusPill>
          </div>
          <div>
            <Button
              variant="secondary"
              size="sm"
              onClick={() => void bridge.openExternalUrl(about.projectUrl)}
            >
              <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
              <span>GitHub Repository</span>
            </Button>
          </div>
        </GlassPanel>

        <GlassPanel title="Software Updates">
          <div className="ds-row-between">
            <span className="ds-label">Release Status</span>
            {updateStatus?.isUpdateAvailable ? (
              <StatusPill tone="active">
                Update Available: v{updateStatus.latestVersion}
              </StatusPill>
            ) : (
              <StatusPill tone="neutral">
                {updateStatus?.state === 'Error'
                  ? 'Check Failed'
                  : `Up to Date (v${about.version})`}
              </StatusPill>
            )}
          </div>
          <div className="ds-row">
            <Button
              variant="secondary"
              size="sm"
              loading={checking}
              onClick={() => void handleCheckUpdates()}
            >
              <RefreshCw size={14} strokeWidth={2} aria-hidden="true" />
              <span>Check for Updates</span>
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={onOpenUpdateModal}
            >
              <Download size={14} strokeWidth={2} aria-hidden="true" />
              <span>
                {updateStatus?.isUpdateAvailable
                  ? 'Install Update'
                  : 'Update Details'}
              </span>
            </Button>
          </div>
        </GlassPanel>
      </div>

      <GlassPanel title="CLI Reference">
        <div className="ds-table-wrap ds-scroller">
          <table className="ds-table">
            <caption className="visually-hidden">
              WinThumbsPreloader command-line flags and usage examples
            </caption>
            <thead>
              <tr>
                <th scope="col">Flag</th>
                <th scope="col">Behavior</th>
                <th scope="col">Example</th>
              </tr>
            </thead>
            <tbody>
              {CLI_REFERENCE.map((row) => (
                <tr key={row.flag}>
                  <td className="ds-mono">{row.flag}</td>
                  <td>{row.description}</td>
                  <td>
                    <div className="ds-row-between">
                      <code className="ds-mono">{row.example}</code>
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={`Copy CLI example for ${row.flag}`}
                        onClick={() => void handleCopyExample(row.example)}
                      >
                        <Copy size={14} strokeWidth={2} aria-hidden="true" />
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </GlassPanel>

      <GlassPanel title="Credits &amp; License">
        <p className="ds-hint" style={{ margin: 0 }}>
          Original WinThumbsPreloader by <strong>Dmitry Bruhov (2018)</strong>.
          WinThumbsPreloader v2/v3 enhancements by{' '}
          <strong>Mutahar Farooq (2026)</strong> and{' '}
          <strong>João Schlickmann (2026)</strong>. Built-in native codecs powered
          by <strong>craft-libs</strong>, <strong>PDFium</strong>, and{' '}
          <strong>resvg</strong>.
        </p>
        <pre className="ds-license-box ds-scroller">{about.licenseText}</pre>
      </GlassPanel>
    </div>
  );
}
