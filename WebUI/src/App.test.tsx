import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { axe } from 'vitest-axe';
import { App } from './App';
import {
  dispatchSyntheticBridgeEvent,
  isTrustedMessageOrigin,
  TRUSTED_ORIGIN,
} from './lib/bridge';
import { resetMockState } from './lib/mockBridge';

function setThemeAttribute(mode: 'dark' | 'light') {
  document.documentElement.dataset.theme = mode;
  document
    .querySelector('meta[name="color-scheme"]')
    ?.setAttribute('content', mode);
}

describe('IPC Origin Validation', () => {
  it('accepts trusted origin and rejects opaque null or spoofed origins', () => {
    expect(isTrustedMessageOrigin(TRUSTED_ORIGIN)).toBe(true);
    expect(
      isTrustedMessageOrigin('https://app.winthumbspreloader.local/index.html')
    ).toBe(true);
    expect(isTrustedMessageOrigin('null')).toBe(false);
    expect(isTrustedMessageOrigin('http://app.winthumbspreloader.local')).toBe(
      false
    );
    expect(
      isTrustedMessageOrigin(
        'https://app.winthumbspreloader.local.attacker.example'
      )
    ).toBe(false);
    expect(
      isTrustedMessageOrigin('data:text/html,<script>alert(1)</script>')
    ).toBe(false);

    const badEvent = new MessageEvent('message', {
      origin: 'https://evil.example',
    });
    expect(isTrustedMessageOrigin(badEvent)).toBe(false);

    const goodEvent = new MessageEvent('message', { origin: TRUSTED_ORIGIN });
    expect(isTrustedMessageOrigin(goodEvent)).toBe(true);
  });
});

describe('Shell, Core Views & Theme Accessibility', () => {
  beforeEach(() => {
    resetMockState();
    localStorage.clear();
    setThemeAttribute('dark');
  });

  it('exercises PreloadView dropzone, manual paths, invalid confirmation, and presets with 0 axe violations', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    expect(
      await screen.findByRole('heading', { name: /Preload & Batch Queue/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: /Skip to main content/i })
    ).toBeInTheDocument();

    // Load an existing preset
    await user.click(screen.getAllByRole('button', { name: /^Load$/i })[0]);
    expect(await screen.findByText('D:\\Photos\\2025')).toBeInTheDocument();

    // Add an invalid path alongside valid paths -> opens partial confirmation dialog
    const pathInput = screen.getByLabelText(/Add Path Manually/i);
    await user.type(pathInput, 'Z:\\Invalid\\MissingFolder');
    await user.click(screen.getByRole('button', { name: 'Add Path' }));

    expect(await screen.findByText('Invalid')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Start Preloading/i }));

    expect(
      await screen.findByRole('heading', {
        name: /Some paths could not be accessed/i,
      })
    ).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: /Continue with valid paths/i })
    );

    // Save a new preset and verify destructive delete dialog focuses Cancel
    const presetInput = screen.getByLabelText(/Save Current As/i);
    await user.clear(presetInput);
    await user.type(presetInput, 'Weekend Shoot');
    await user.click(screen.getByRole('button', { name: /^Save$/i }));
    expect(await screen.findByText('Weekend Shoot')).toBeInTheDocument();

    await user.click(
      screen.getByRole('button', { name: /Delete preset Weekend Shoot/i })
    );
    const cancelBtn = await screen.findByRole('button', { name: /^Cancel$/i });
    expect(document.activeElement).toBe(cancelBtn);
    await user.click(cancelBtn);

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('exercises CacheView telemetry, reset alert banner, force-restore modal, and clear path with 0 axe violations', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    await user.click(screen.getByRole('button', { name: /^Cache$/i }));
    expect(
      await screen.findByRole('heading', { name: /Cache Manager/i })
    ).toBeInTheDocument();

    // Open Force Restore destructive modal -> Cancel receives initial focus
    await user.click(
      screen.getByRole('button', { name: /Force Restore \(Restart Explorer\)/i })
    );
    const cancelBtn = await screen.findByRole('button', { name: /^Cancel$/i });
    expect(document.activeElement).toBe(cancelBtn);
    await user.click(cancelBtn);

    // Clear backup folder -> Auto Backup & Auto Restore become disabled
    const autoBackupSwitch = screen.getByRole('switch', {
      name: /Auto Backup$/i,
    });
    expect(autoBackupSwitch).toBeEnabled();
    await user.click(screen.getByRole('button', { name: /Clear Path/i }));
    await waitFor(() => {
      expect(
        screen.getByRole('switch', { name: /Auto Backup$/i })
      ).toBeDisabled();
      expect(
        screen.getByRole('switch', { name: /Auto Restore$/i })
      ).toBeDisabled();
    });

    // Save Policy
    await user.click(screen.getByRole('button', { name: /Save Policy/i }));

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('exercises ScheduleView triggers, time modes, drive selection transfer, and 0 axe violations', async () => {
    const user = userEvent.setup();
    const { container } = render(<App />);

    await user.click(screen.getByRole('button', { name: /^Schedule$/i }));
    expect(
      await screen.findByRole('heading', { name: /Scheduled Preloading/i })
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Week$/i }));
    await user.click(screen.getByRole('switch', { name: /24-Hour Time/i }));
    expect(screen.getByLabelText(/Hour \(24-hour\)/i)).toBeInTheDocument();

    await user.click(
      screen.getByRole('switch', { name: /Detailed Time \(Minutes\)/i })
    );
    expect(screen.getByLabelText(/^Minute$/i)).toBeInTheDocument();

    // Toggling Enable Drive Selection off transfers checked D:\ into Custom Paths
    await user.click(
      screen.getByRole('switch', { name: /Enable Drive Selection/i })
    );
    const customPaths = screen.getByLabelText(
      /Custom Paths/i
    ) as HTMLTextAreaElement;
    expect(customPaths.value).toContain('D:\\');

    await user.click(
      screen.getByRole('button', { name: /Save Scheduled Task/i })
    );

    expect(await axe(container)).toHaveNoViolations();
    setThemeAttribute('light');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('shows Update Available pill on update:available event, opens UpdateModal, and never wipes pinned theme on OS change', async () => {
    const listeners: Array<(e: MediaQueryListEvent) => void> = [];
    const originalMatchMedia = window.matchMedia;
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: (query: string) => ({
        matches: true,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: (_: string, cb: (e: MediaQueryListEvent) => void) => {
          listeners.push(cb);
        },
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }),
    });

    try {
      const user = userEvent.setup();
      const { container } = render(<App />);

      await user.click(screen.getByRole('button', { name: /^Settings$/i }));
      expect(
        await screen.findByRole('heading', { name: /^Settings$/i })
      ).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /^Light$/i }));
      expect(localStorage.getItem('wtp-theme')).toBe('light');
      expect(document.documentElement.dataset.theme).toBe('light');

      // OS changes to light and then back to dark -> pinned 'light' override MUST remain intact
      act(() => {
        for (const cb of listeners) {
          cb({ matches: false } as MediaQueryListEvent);
        }
      });
      expect(localStorage.getItem('wtp-theme')).toBe('light');
      expect(document.documentElement.dataset.theme).toBe('light');

      // Push update:available event and open UpdateModal
      act(() => {
        dispatchSyntheticBridgeEvent('update:available', {
          state: 'UpdateAvailable',
          currentVersion: '3.3.0',
          latestVersion: '3.4.0',
          releaseTag: 'v3.4.0',
          releasePageUrl:
            'https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases/tag/v3.4.0',
          hasInstallerAsset: true,
          installerFileName: 'WinThumbsPreloader-Setup-3.4.0.exe',
          installerSize: 15_728_640,
          isUpdateAvailable: true,
        });
      });

      const updatePill = await screen.findByRole('button', {
        name: /Update Available/i,
      });
      await user.click(updatePill);

      expect(
        await screen.findByRole('heading', { name: /Application Update/i })
      ).toBeInTheDocument();
      expect(screen.getByText('v3.4.0')).toBeInTheDocument();
      expect(await axe(container)).toHaveNoViolations();

      await user.click(screen.getByRole('button', { name: /^Dismiss$/i }));
    } finally {
      Object.defineProperty(window, 'matchMedia', {
        writable: true,
        value: originalMatchMedia,
      });
    }
  });

  it('preserves CacheView unsaved drafts during telemetry ticks and keeps PreloadView queue across tab switches', async () => {
    const user = userEvent.setup();
    render(<App />);

    // Stage a path in PreloadView, switch to Settings, and return -> queue must stay intact
    expect(
      await screen.findByRole('heading', { name: /Preload & Batch Queue/i })
    ).toBeInTheDocument();
    const pathInput = screen.getByLabelText(/Add Path Manually/i);
    await user.type(pathInput, 'E:\\\\MediaLibrary');
    await user.click(screen.getByRole('button', { name: 'Add Path' }));
    expect(await screen.findByText('E:\\\\MediaLibrary')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Settings$/i }));
    expect(
      await screen.findByRole('heading', { name: /^Settings$/i })
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^Preload$/i }));
    expect(await screen.findByText('E:\\\\MediaLibrary')).toBeInTheDocument();

    // Navigate to CacheView, edit Backup Folder Path, push live cache:telemetry event -> draft must not reset
    await user.click(screen.getByRole('button', { name: /^Cache$/i }));
    expect(
      await screen.findByRole('heading', { name: /Cache Manager/i })
    ).toBeInTheDocument();

    const folderInput = screen.getByLabelText(
      /Backup Folder Path/i
    ) as HTMLInputElement;
    await user.clear(folderInput);
    await user.type(folderInput, 'D:\\\\CustomBackupDraft');
    expect(folderInput.value).toBe('D:\\\\CustomBackupDraft');

    const snapshot = await waitFor(() => {
      expect(screen.getAllByText('936 MB').length).toBeGreaterThanOrEqual(1);
      return true;
    });
    expect(snapshot).toBe(true);

    act(() => {
      dispatchSyntheticBridgeEvent('cache:telemetry', {
        explorerSizeBytes: 1_000_000_000,
        explorerSizeFormatted: '954 MB',
        backupSizeBytesNullable: 800_000_000,
        backupSizeFormatted: '763 MB',
        backupFolderPath: 'C:\\\\OriginalServerPath',
        backupFolderStatus: {
          status: 'Writable',
          message: 'Status: Writable.',
          normalizedPath: 'C:\\\\OriginalServerPath',
          isRunningAsAdmin: false,
          canUseForBackup: true,
        },
        recommendedDefaultBackupPath: 'C:\\\\Recommended',
        lastBackupDateTimeIso: null,
        resetRecognized: false,
        backupLargerThanExplorer: false,
        lockHeld: false,
        startWithWindows: true,
        autoBackup: true,
        autoRestore: false,
        alertOnReset: true,
        autoBackupAfterPreload: true,
        backupRestoreClearSafety: 'Allow Partial Copy',
        explorerCacheDeletionMethod: 'Manual Deletion',
        explorerCloseFrequency: 'On Every File',
        cacheSizeFormat: 'MB',
        cacheUpdateIntervalMs: 250,
        autoBackupIntervalMs: 5000,
        autoRestoreIntervalMs: 5000,
        buckets: [],
      });
    });

    const updatedSizes = await screen.findAllByText('954 MB');
    expect(updatedSizes.length).toBeGreaterThanOrEqual(1);
    expect(folderInput.value).toBe('D:\\\\CustomBackupDraft');
  });
});
