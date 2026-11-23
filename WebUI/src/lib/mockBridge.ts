import type {
  AdvancedCacheSettingsRequest,
  BootstrapData,
  BulkPreset,
  CacheBucketInfo,
  CacheSnapshot,
  CodecsStateDto,
  ExtensionsSaveRequest,
  GeneralSettingsSaveRequest,
  LogFolderStatusDto,
  LoggingSaveRequest,
  OptimizerProfileDto,
  OptimizerStateDto,
  PreloadInitialState,
  PreloadPathValidationItem,
  PreloadRunRequest,
  ScheduleDriveInfo,
  ScheduleSaveRequest,
  ScheduleState,
  SettingsStateDto,
  ThemeMode,
} from './types';
import { dispatchSyntheticBridgeEvent } from './bridge';
import {
  buildInitialCodecsState,
  buildInitialOptimizerState,
  buildInitialSettingsState,
  buildOptimizerTweaks,
  DEFAULT_EXTENSIONS_TEXT,
  organizeExtensionsMock,
} from './mockData';

const STREAM_NAMES = [
  '16',
  '32',
  '48',
  '96',
  '256',
  '768',
  '1280',
  '1920',
  '2560',
  'sr',
  'wide',
  'exif',
  'wide_alternate',
  'custom_stream',
];

const SOFT_LIMIT = 1_600 * 1024 * 1024;

function formatBytes(bytes: number, format: string): string {
  if (bytes <= 0) return '0.00 MB';
  const kb = bytes / 1024;
  const mb = kb / 1024;
  const gb = mb / 1024;
  switch (format) {
    case 'KB':
      return `${Math.round(kb).toLocaleString('en-US')} KB`;
    case 'GB':
      return `${gb.toFixed(2)} GB`;
    case 'Auto':
      if (gb >= 1) return `${gb.toFixed(2)} GB`;
      if (mb >= 1) return `${mb.toFixed(mb < 10 ? 2 : 1)} MB`;
      return `${kb.toFixed(1)} KB`;
    case 'MB':
    default:
      return `${mb.toFixed(mb < 10 ? 2 : mb < 100 ? 1 : 0)} MB`;
  }
}

function buildBuckets(): CacheBucketInfo[] {
  const sizesMb: Record<string, number> = {
    '16': 12,
    '32': 24,
    '48': 36,
    '96': 142,
    '256': 418,
    '768': 210,
    '1280': 64,
    '1920': 0,
    '2560': 0,
    sr: 4,
    wide: 18,
    exif: 6,
    wide_alternate: 2,
    custom_stream: 0,
  };
  return STREAM_NAMES.map((size) => {
    const sizeBytes = (sizesMb[size] ?? 0) * 1024 * 1024;
    return {
      size,
      sizeBytes,
      softLimitBytes: SOFT_LIMIT,
      saturated: sizeBytes >= SOFT_LIMIT,
    };
  });
}

function buildAvailableDrives(): ScheduleDriveInfo[] {
  const mounted: Record<string, { driveType: string; label: string }> = {
    C: { driveType: 'Fixed', label: 'System' },
    D: { driveType: 'Fixed', label: 'Media Archive' },
    E: { driveType: 'Removable', label: 'Photos SD' },
    Z: { driveType: 'Network', label: 'NAS Share' },
  };
  return 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('').map((letter) => {
    const info = mounted[letter];
    return {
      letter,
      root: `${letter}:\\`,
      isMounted: Boolean(info),
      driveType: info?.driveType ?? 'Unmounted',
      label: info?.label ?? '',
    };
  });
}

interface MockState {
  theme: ThemeMode;
  presets: BulkPreset[];
  cache: CacheSnapshot;
  schedule: ScheduleState;
  codecs: CodecsStateDto;
  optimizer: OptimizerStateDto;
  settings: SettingsStateDto;
}

function createInitialMockState(): MockState {
  const recommendedBackup =
    'C:\\Users\\Demo\\AppData\\Local\\WinThumbsPreloader\\CacheBackup';
  const buckets = buildBuckets();
  const explorerBytes = buckets.reduce((acc, b) => acc + b.sizeBytes, 0);
  const backupBytes = Math.round(explorerBytes * 0.92);

  return {
    theme: 'dark',
    presets: [
      {
        name: 'Photography Archive',
        paths: ['D:\\Photos\\2025', 'D:\\Photos\\2026'],
        recursive: true,
        multiThreaded: true,
        threads: 0,
        maximumInstances: 0,
        method: 'Sequentially',
      },
      {
        name: 'NAS Renders',
        paths: ['\\\\NAS\\Renders\\Showcase'],
        recursive: true,
        multiThreaded: true,
        threads: 8,
        maximumInstances: 2,
        method: 'Simultaneously',
      },
    ],
    cache: {
      explorerSizeBytes: explorerBytes,
      explorerSizeFormatted: formatBytes(explorerBytes, 'MB'),
      backupSizeBytesNullable: backupBytes,
      backupSizeFormatted: formatBytes(backupBytes, 'MB'),
      backupFolderPath: recommendedBackup,
      backupFolderStatus: {
        status: 'Writable',
        message: 'Status: Writable.',
        normalizedPath: recommendedBackup,
        isRunningAsAdmin: false,
        canUseForBackup: true,
      },
      recommendedDefaultBackupPath: recommendedBackup,
      lastBackupDateTimeIso: new Date(Date.now() - 3_600_000).toISOString(),
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
      buckets,
    },
    schedule: {
      taskExists: true,
      enabled: true,
      frequency: 'Day',
      hour: 3,
      minute: 0,
      onIdle: true,
      multithreaded: true,
      threads: 0,
      drives: ['D'],
      customPaths: ['D:\\Media\\Incoming'],
      lastRunTimeText: 'Last run time: Today at 3:00 AM',
      nextRunTimeText: 'Next run time: Tomorrow at 3:00 AM',
      statusMessage: 'Task is scheduled and enabled.',
      driveSelectionEnabled: true,
      driveSortChoice: 'Vertical',
      twentyFourHourMode: false,
      detailedTimeMode: false,
      availableDrives: buildAvailableDrives(),
    },
    codecs: buildInitialCodecsState(),
    optimizer: buildInitialOptimizerState(),
    settings: buildInitialSettingsState(),
  };
}

let mockState: MockState = createInitialMockState();

export function resetMockState(): void {
  mockState = createInitialMockState();
}

function validatePathItem(raw: string): PreloadPathValidationItem {
  const cleaned = raw.trim().replace(/^"+|"+$/g, '');
  const isUnc = cleaned.startsWith('\\\\');
  const isDrive = /^[a-zA-Z]:\\/.test(cleaned) || /^[a-zA-Z]:$/.test(cleaned);
  const looksInvalid =
    !cleaned ||
    cleaned.toLowerCase().includes('invalid') ||
    cleaned.toLowerCase().includes('missing') ||
    (!isUnc && !isDrive && !cleaned.startsWith('/'));

  const normalized = looksInvalid
    ? null
    : /^[a-zA-Z]:$/.test(cleaned)
      ? `${cleaned.toUpperCase()}\\`
      : cleaned;

  return {
    input: cleaned,
    normalizedPath: normalized,
    valid: !looksInvalid,
    isNetwork: isUnc || cleaned.toUpperCase().startsWith('Z:'),
  };
}

function syncCacheFormatting(): void {
  const c = mockState.cache;
  c.explorerSizeFormatted = formatBytes(c.explorerSizeBytes, c.cacheSizeFormat);
  c.backupSizeFormatted =
    c.backupSizeBytesNullable !== null
      ? formatBytes(c.backupSizeBytesNullable, c.cacheSizeFormat)
      : 'N/A';
  c.backupLargerThanExplorer =
    c.backupSizeBytesNullable !== null &&
    c.backupSizeBytesNullable > c.explorerSizeBytes;
}

function evaluateMockLogFolderStatus(rawPath: string): LogFolderStatusDto {
  const trimmed = rawPath.trim();
  if (!trimmed) {
    return {
      status: 'NotSelected',
      message: 'Status: No log folder selected.',
      normalizedPath: '',
      isRunningAsAdmin: false,
      usesDefaultPath: false,
      canUseForLogging: false,
    };
  }
  const isRestricted =
    trimmed.toLowerCase().includes('program files') ||
    trimmed.toLowerCase().includes('c:\\windows');
  if (isRestricted) {
    return {
      status: 'WritableAdmin',
      message: 'Status: Administrator privileges may be required to write logs here.',
      normalizedPath: trimmed,
      isRunningAsAdmin: false,
      usesDefaultPath: false,
      canUseForLogging: true,
    };
  }
  return {
    status: 'Writable',
    message: 'Status: Writable.',
    normalizedPath: trimmed,
    isRunningAsAdmin: false,
    usesDefaultPath:
      trimmed.toLowerCase() ===
      mockState.settings.logging.recommendedDefaultLogPath.toLowerCase(),
    canUseForLogging: true,
  };
}

export function emitMockEvent(_eventName: string, _payload: unknown): void {
  // Hook point for browser tests
}

export async function handleMockCommand<T>(
  cmd: string,
  payload: unknown
): Promise<T> {
  const p = (payload && typeof payload === 'object' ? payload : {}) as Record<
    string,
    unknown
  >;

  switch (cmd) {
    case 'app.getBootstrap': {
      const dark =
        mockState.theme === 'dark' ||
        (mockState.theme === 'system' &&
          typeof window !== 'undefined' &&
          window.matchMedia?.('(prefers-color-scheme: dark)').matches);
      return {
        appName: 'WinThumbsPreloader',
        version: '3.3.0',
        isRunningAsAdmin: false,
        theme: mockState.theme,
        effectiveDarkMode: Boolean(dark),
        initialView: 'preload',
        isWebView2: false,
        invalidLaunchPathWarning: null,
      } satisfies BootstrapData as T;
    }

    case 'app.setTheme': {
      const theme = (p.theme as ThemeMode) || 'dark';
      mockState.theme = theme;
      const effectiveDarkMode =
        theme === 'dark' ||
        (theme === 'system' &&
          typeof window !== 'undefined' &&
          Boolean(window.matchMedia?.('(prefers-color-scheme: dark)').matches));
      return { ok: true, theme, effectiveDarkMode } as T;
    }

    case 'app.minimizeToTray':
    case 'app.quit':
      return { ok: true } as T;

    case 'app.openExternalUrl':
      return { ok: true, url: String(p.url ?? '') } as T;

    case 'app.checkForUpdates':
      return {
        state: 'Updated',
        currentVersion: '3.3.0',
        latestVersion: '3.3.0',
        releaseTag: 'v3.3.0',
        releasePageUrl:
          'https://github.com/joaoschlickmann/WinThumbsPreloader-V3/releases',
        hasInstallerAsset: false,
        installerFileName: '',
        installerSize: 0,
        isUpdateAvailable: false,
      } as T;

    case 'app.downloadAndInstallUpdate':
      return {
        ok: false,
        launched: false,
        cancelledByUser: false,
        openedReleasePageFallback: false,
        message: 'WinThumbsPreloader is already up to date.',
        releasePageUrl: null,
      } as T;

    case 'dragDrop.resolve': {
      const rawPaths = Array.isArray(p.paths) ? (p.paths as string[]) : [];
      const items = rawPaths.map(validatePathItem);
      return {
        items,
        paths: items
          .filter((i) => i.valid && i.normalizedPath)
          .map((i) => i.normalizedPath as string),
      } as T;
    }

    case 'preload.getState':
      return {
        defaultMultithreaded: mockState.settings.general.multithreaded,
        defaultThreadCount: mockState.settings.general.threadCount,
        savedPresets: [...mockState.presets],
        invalidLaunchPathWarning: null,
      } satisfies PreloadInitialState as T;

    case 'preload.pickFolder':
      return {
        cancelled: false,
        path: 'D:\\Photos\\2026',
        paths: ['D:\\Photos\\2026'],
      } as T;

    case 'preload.validatePaths': {
      const rawPaths = Array.isArray(p.paths) ? (p.paths as string[]) : [];
      return { items: rawPaths.map(validatePathItem) } as T;
    }

    case 'preload.start': {
      const req = p as unknown as PreloadRunRequest;
      const items = (req.paths ?? []).map(validatePathItem);
      const validPaths = items
        .filter((i) => i.valid && i.normalizedPath)
        .map((i) => i.normalizedPath as string);
      const invalidPaths = items.filter((i) => !i.valid).map((i) => i.input);

      if (items.length === 0) {
        return {
          ok: false,
          requiresConfirmation: false,
          message: 'Please add at least one folder or file path to preload.',
          launchedPaths: [],
          invalidPaths: [],
        } as T;
      }
      if (invalidPaths.length > 0 && validPaths.length === 0) {
        return {
          ok: false,
          requiresConfirmation: false,
          message: 'None of the provided paths exist or could be accessed.',
          launchedPaths: [],
          invalidPaths,
        } as T;
      }
      if (invalidPaths.length > 0 && !req.skipInvalid) {
        return {
          ok: false,
          requiresConfirmation: true,
          message: `Some paths could not be found or accessed. Continue with the remaining ${validPaths.length} valid path(s)?`,
          launchedPaths: validPaths,
          invalidPaths,
        } as T;
      }
      return {
        ok: true,
        requiresConfirmation: false,
        message: `Preloader started for ${validPaths.length} path(s).`,
        launchedPaths: validPaths,
        invalidPaths,
      } as T;
    }

    case 'preload.savePreset': {
      const incoming = p as unknown as BulkPreset;
      const name = (incoming.name ?? '').trim();
      if (!name) {
        return {
          ok: false,
          message: 'Please enter a name for the preset.',
          savedPresets: [...mockState.presets],
        } as T;
      }
      if (!incoming.paths || incoming.paths.length === 0) {
        return {
          ok: false,
          message: 'Please add at least one path before saving a preset.',
          savedPresets: [...mockState.presets],
        } as T;
      }
      const normalized: BulkPreset = {
        name,
        paths: [...incoming.paths],
        recursive: Boolean(incoming.recursive),
        multiThreaded: Boolean(incoming.multiThreaded),
        threads: Number(incoming.threads ?? 0),
        maximumInstances: Number(incoming.maximumInstances ?? 0),
        method:
          incoming.method === 'Simultaneously' ? 'Simultaneously' : 'Sequentially',
      };
      const idx = mockState.presets.findIndex(
        (item) => item.name.toLowerCase() === name.toLowerCase()
      );
      if (idx >= 0) {
        mockState.presets[idx] = normalized;
      } else {
        mockState.presets.push(normalized);
      }
      mockState.presets.sort((a, b) => a.name.localeCompare(b.name));
      return {
        ok: true,
        message: `Preset '${name}' saved.`,
        savedPresets: [...mockState.presets],
      } as T;
    }

    case 'preload.deletePreset': {
      const target = String(p.name ?? '').trim();
      const before = mockState.presets.length;
      mockState.presets = mockState.presets.filter(
        (item) => item.name.toLowerCase() !== target.toLowerCase()
      );
      const ok = mockState.presets.length < before;
      return {
        ok,
        message: ok
          ? `Preset '${target}' deleted.`
          : `Preset '${target}' not found.`,
        savedPresets: [...mockState.presets],
      } as T;
    }

    case 'preload.exportPaths':
      return {
        ok: true,
        cancelled: false,
        path: 'C:\\Users\\Demo\\Documents\\BulkPreloaderPaths.txt',
        message: 'Paths exported.',
      } as T;

    case 'preload.exportPresets':
      return {
        ok: true,
        cancelled: false,
        path: 'C:\\Users\\Demo\\Documents\\BulkPreloaderPresets.json',
        message: 'Presets exported.',
      } as T;

    case 'cache.getState':
      syncCacheFormatting();
      return { ...mockState.cache } as T;

    case 'cache.backup': {
      if (!mockState.cache.backupFolderPath) {
        return {
          ok: false,
          status: 'Failure',
          failureReason: 'BackupPathNotConfigured',
          message: 'Select a backup folder first.',
          snapshot: { ...mockState.cache },
        } as T;
      }
      dispatchSyntheticBridgeEvent('cache:progress', {
        operation: 'backup',
        percent: 50,
      });
      mockState.cache.backupSizeBytesNullable = mockState.cache.explorerSizeBytes;
      mockState.cache.lastBackupDateTimeIso = new Date().toISOString();
      mockState.cache.resetRecognized = false;
      syncCacheFormatting();
      dispatchSyntheticBridgeEvent('cache:progress', {
        operation: 'backup',
        percent: 100,
      });
      return {
        ok: true,
        status: 'Success',
        failureReason: 'None',
        message: 'Thumbnail cache backup finished.',
        snapshot: { ...mockState.cache },
      } as T;
    }

    case 'cache.restore': {
      if (!mockState.cache.backupFolderPath) {
        return {
          ok: false,
          status: 'Failure',
          failureReason: 'BackupPathNotConfigured',
          message: 'No backup folder selected.',
          snapshot: { ...mockState.cache },
        } as T;
      }
      const force = Boolean(p.force);
      mockState.cache.explorerSizeBytes =
        mockState.cache.backupSizeBytesNullable ??
        mockState.cache.explorerSizeBytes;
      mockState.cache.resetRecognized = false;
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Success',
        failureReason: 'None',
        message: force
          ? 'Thumbnail cache force-restored.'
          : 'Thumbnail cache restored.',
        snapshot: { ...mockState.cache },
      } as T;
    }

    case 'cache.clearBackup':
      mockState.cache.backupSizeBytesNullable = 0;
      mockState.cache.lastBackupDateTimeIso = null;
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Success',
        failureReason: 'None',
        message: 'Backup cleared.',
        snapshot: { ...mockState.cache },
      } as T;

    case 'cache.clearCache':
      mockState.cache.explorerSizeBytes = 0;
      mockState.cache.buckets = mockState.cache.buckets.map((b) => ({
        ...b,
        sizeBytes: 0,
        saturated: false,
      }));
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Success',
        failureReason: 'None',
        message: 'Explorer cache cleared.',
        snapshot: { ...mockState.cache },
      } as T;

    case 'cache.pickBackupFolder': {
      const picked =
        'C:\\Users\\Demo\\AppData\\Local\\WinThumbsPreloader\\CacheBackup';
      return {
        cancelled: false,
        path: picked,
        status: {
          status: 'Writable',
          message: 'Status: Writable.',
          normalizedPath: picked,
          isRunningAsAdmin: false,
          canUseForBackup: true,
        },
      } as T;
    }

    case 'cache.setBackupFolder': {
      const nextPath = typeof p.path === 'string' ? p.path.trim() : '';
      if (!nextPath) {
        const disabledAuto =
          mockState.cache.autoBackup || mockState.cache.autoRestore;
        mockState.cache.backupFolderPath = '';
        mockState.cache.backupSizeBytesNullable = null;
        mockState.cache.autoBackup = false;
        mockState.cache.autoRestore = false;
        mockState.cache.backupFolderStatus = {
          status: 'NotSelected',
          message: 'Status: No backup folder selected.',
          normalizedPath: '',
          isRunningAsAdmin: false,
          canUseForBackup: false,
        };
        syncCacheFormatting();
        return {
          ok: true,
          status: 'NotSelected',
          message: disabledAuto
            ? 'Backup path cleared. Auto backup/restore disabled.'
            : 'Backup path cleared.',
          path: '',
          disabledAutoToggles: disabledAuto,
          requiresConfirmation: false,
        } as T;
      }
      mockState.cache.backupFolderPath = nextPath;
      mockState.cache.backupFolderStatus = {
        status: 'Writable',
        message: 'Status: Writable.',
        normalizedPath: nextPath,
        isRunningAsAdmin: false,
        canUseForBackup: true,
      };
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Writable',
        message: 'Backup folder saved.',
        path: nextPath,
        disabledAutoToggles: false,
        requiresConfirmation: false,
      } as T;
    }

    case 'cache.openCacheFolder':
      return {
        ok: true,
        status: 'Opened',
        failureReason: 'None',
        message: 'Explorer cache folder opened.',
        snapshot: null,
      } as T;

    case 'cache.openBackupFolder':
      return {
        ok: Boolean(mockState.cache.backupFolderPath),
        status: mockState.cache.backupFolderPath ? 'Opened' : 'Failed',
        failureReason: mockState.cache.backupFolderPath
          ? 'None'
          : 'BackupPathNotConfigured',
        message: mockState.cache.backupFolderPath
          ? 'Backup folder opened.'
          : 'No backup folder configured.',
        snapshot: null,
      } as T;

    case 'cache.setStartWithWindows':
      mockState.cache.startWithWindows = Boolean(p.enabled);
      return {
        ok: true,
        status: 'Updated',
        failureReason: 'None',
        message: mockState.cache.startWithWindows
          ? 'Start with Windows enabled.'
          : 'Start with Windows disabled.',
        snapshot: { ...mockState.cache },
      } as T;

    case 'cache.setAutoFlags': {
      const autoBackup = p.autoBackup as boolean | null | undefined;
      const autoRestore = p.autoRestore as boolean | null | undefined;
      const alertOnReset = p.alertOnReset as boolean | null | undefined;
      if (
        (autoBackup === true || autoRestore === true) &&
        !mockState.cache.backupFolderPath
      ) {
        return {
          ok: false,
          status: 'BackupPathRequired',
          failureReason: 'BackupPathNotConfigured',
          message:
            'Select a backup folder before enabling Auto Backup or Auto Restore.',
          snapshot: { ...mockState.cache },
        } as T;
      }
      if (typeof autoBackup === 'boolean')
        mockState.cache.autoBackup = autoBackup;
      if (typeof autoRestore === 'boolean')
        mockState.cache.autoRestore = autoRestore;
      if (typeof alertOnReset === 'boolean')
        mockState.cache.alertOnReset = alertOnReset;
      return {
        ok: true,
        status: 'Updated',
        failureReason: 'None',
        message: 'Cache automation flags updated.',
        snapshot: { ...mockState.cache },
      } as T;
    }

    case 'cache.saveAdvanced': {
      const req = p as AdvancedCacheSettingsRequest;
      if (req.autoBackupAfterPreload !== undefined)
        mockState.cache.autoBackupAfterPreload = req.autoBackupAfterPreload;
      if (req.explorerCacheDeletionMethod)
        mockState.cache.explorerCacheDeletionMethod =
          req.explorerCacheDeletionMethod;
      if (req.explorerCloseFrequency)
        mockState.cache.explorerCloseFrequency = req.explorerCloseFrequency;
      if (req.backupRestoreClearSafety)
        mockState.cache.backupRestoreClearSafety = req.backupRestoreClearSafety;
      if (req.cacheSizeFormat)
        mockState.cache.cacheSizeFormat = req.cacheSizeFormat;
      if (req.cacheUpdateIntervalMs !== undefined)
        mockState.cache.cacheUpdateIntervalMs = req.cacheUpdateIntervalMs;
      if (req.autoBackupIntervalMs !== undefined)
        mockState.cache.autoBackupIntervalMs = req.autoBackupIntervalMs;
      if (req.autoRestoreIntervalMs !== undefined)
        mockState.cache.autoRestoreIntervalMs = req.autoRestoreIntervalMs;
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Saved',
        failureReason: 'None',
        message: 'Advanced cache settings saved.',
        snapshot: { ...mockState.cache },
      } as T;
    }

    case 'cache.resetAdvanced': {
      const scope = String(p.scope ?? 'all').toLowerCase();
      if (scope === 'all' || scope === 'general') {
        mockState.cache.cacheUpdateIntervalMs = 250;
        mockState.cache.autoBackupIntervalMs = 5000;
        mockState.cache.autoRestoreIntervalMs = 5000;
        mockState.cache.cacheSizeFormat = 'MB';
      }
      if (scope === 'all' || scope === 'cache') {
        mockState.cache.autoBackupAfterPreload = true;
        mockState.cache.backupRestoreClearSafety = 'Allow Partial Copy';
        mockState.cache.explorerCacheDeletionMethod = 'Manual Deletion';
        mockState.cache.explorerCloseFrequency = 'On Every File';
      }
      if (scope === 'backup') {
        mockState.cache.backupFolderPath =
          mockState.cache.recommendedDefaultBackupPath;
      }
      syncCacheFormatting();
      return {
        ok: true,
        status: 'Reset',
        failureReason: 'None',
        message: 'Advanced cache defaults restored.',
        snapshot: { ...mockState.cache },
      } as T;
    }

    case 'schedule.getState':
      return { ...mockState.schedule } as T;

    case 'schedule.setEnabled': {
      if (!mockState.schedule.taskExists) {
        return {
          ok: false,
          message: 'Scheduled task does not exist yet. Save a task first.',
          state: { ...mockState.schedule },
        } as T;
      }
      mockState.schedule.enabled = Boolean(p.enabled);
      return {
        ok: true,
        message: mockState.schedule.enabled
          ? 'Scheduled task enabled.'
          : 'Scheduled task disabled.',
        state: { ...mockState.schedule },
      } as T;
    }

    case 'schedule.saveTask': {
      const req = p as unknown as ScheduleSaveRequest;
      const hasTargets =
        (req.drives?.length ?? 0) > 0 || (req.customPaths?.length ?? 0) > 0;
      if (!hasTargets) {
        return {
          ok: false,
          message:
            'Please select at least one drive letter or provide a valid path before saving the task.',
          state: { ...mockState.schedule },
        } as T;
      }
      mockState.schedule = {
        ...mockState.schedule,
        taskExists: true,
        enabled: req.enabled,
        frequency: req.frequency,
        hour: req.hour,
        minute: req.minute,
        onIdle: req.onIdle,
        multithreaded: req.multithreaded,
        threads: req.threads,
        drives: [...req.drives],
        customPaths: [...req.customPaths],
        lastRunTimeText: 'The task has not run yet.',
        nextRunTimeText: 'Next run time: Scheduled',
        statusMessage: 'Task saved successfully.',
      };
      return {
        ok: true,
        message: 'Task saved successfully.',
        state: { ...mockState.schedule },
      } as T;
    }

    case 'schedule.deleteTask':
      mockState.schedule = {
        ...mockState.schedule,
        taskExists: false,
        enabled: false,
        lastRunTimeText: 'Task not found.',
        nextRunTimeText: '',
        statusMessage: 'Task deleted successfully.',
      };
      return {
        ok: true,
        message: 'Task deleted successfully.',
        state: { ...mockState.schedule },
      } as T;

    case 'schedule.savePreferences':
      mockState.schedule = {
        ...mockState.schedule,
        driveSelectionEnabled: Boolean(p.driveSelectionEnabled),
        driveSortChoice:
          p.driveSortChoice === 'Horizontal' ? 'Horizontal' : 'Vertical',
        twentyFourHourMode: Boolean(p.twentyFourHourMode),
        detailedTimeMode: Boolean(p.detailedTimeMode),
        statusMessage: 'Schedule display preferences saved.',
      };
      return {
        ok: true,
        message: 'Schedule display preferences saved.',
        state: { ...mockState.schedule },
      } as T;

    case 'schedule.resetPreferences':
      mockState.schedule = {
        ...mockState.schedule,
        driveSelectionEnabled: true,
        driveSortChoice: 'Vertical',
        twentyFourHourMode: false,
        detailedTimeMode: false,
        statusMessage: 'Schedule display preferences saved.',
      };
      return {
        ok: true,
        message: 'Schedule display preferences saved.',
        state: { ...mockState.schedule },
      } as T;

    case 'schedule.openTaskScheduler':
      return { ok: true, message: 'Task Scheduler opened.', state: null } as T;

    /* ---- Codecs ---- */
    case 'codecs.getState':
      return { ...mockState.codecs } as T;

    case 'codecs.apply': {
      const mask = Number(p.mask ?? 0) & 0x3ff;
      const elevateViaUac = Boolean(p.elevateViaUac);
      const syncExtensions = Boolean(p.syncExtensions);
      const addedExts: string[] = [];

      mockState.codecs = {
        ...mockState.codecs,
        enabledMask: mask,
        hasMachineLevelCodecsOrBackup:
          mockState.codecs.hasMachineLevelCodecsOrBackup || elevateViaUac,
        families: mockState.codecs.families.map((f) => {
          const on = (mask & (1 << f.bitIndex)) !== 0;
          if (on) {
            for (const ext of f.extensions) {
              addedExts.push(ext.replace(/^\./, ''));
            }
          }
          return {
            ...f,
            isEnabled: on,
            isPartiallyEnabled: false,
            hasExternalHandler: !on && f.hasExternalHandler,
            currentHandlerName: on
              ? 'WinThumbsCodecs'
              : f.hasExternalHandler
                ? f.currentHandlerName
                : 'None',
            statusText: on
              ? 'Active'
              : f.hasExternalHandler
                ? 'External'
                : 'Not Installed',
          };
        }),
      };

      if (syncExtensions && mask !== 0) {
        const current = mockState.settings.extensions.extensionsText
          .split(/[,\s\r\n]+/)
          .map((s) => s.trim().toLowerCase())
          .filter(Boolean);
        for (const ext of addedExts) {
          if (!current.includes(ext.toLowerCase())) {
            current.push(ext.toLowerCase());
          }
        }
        mockState.settings.extensions.extensionsText = organizeExtensionsMock(
          current.join(', '),
          mockState.settings.extensions.extensionsAutoFormatting === 'Disabled'
            ? 'CommasAndSpaces'
            : mockState.settings.extensions.extensionsAutoFormatting
        );
      }

      const hexMask = mask.toString(16).toUpperCase().padStart(3, '0');
      return {
        ok: true,
        uacCancelled: false,
        machineUninstallSkipped: false,
        extensionsSynced: syncExtensions && mask !== 0,
        appliedMask: mask,
        message: `Thumbnail codecs updated (mask 0x${hexMask}).`,
        scopeNotice:
          !elevateViaUac &&
          !mockState.codecs.isElevated &&
          mockState.settings.general.admin
            ? "'Run As Admin' is enabled in Settings. Use 'Elevate & Apply (UAC)' so elevated preload processes also see these codecs."
            : null,
        state: { ...mockState.codecs },
      } as T;
    }

    case 'codecs.uninstallAll':
      mockState.codecs = {
        ...mockState.codecs,
        enabledMask: 0,
        hasMachineLevelCodecsOrBackup: false,
        families: mockState.codecs.families.map((f) => ({
          ...f,
          isEnabled: false,
          isPartiallyEnabled: false,
          currentHandlerName: f.hasExternalHandler ? f.currentHandlerName : 'None',
          statusText: f.hasExternalHandler ? 'External' : 'Not Installed',
        })),
      };
      return {
        ok: true,
        uacCancelled: false,
        machineUninstallSkipped: false,
        extensionsSynced: false,
        appliedMask: 0,
        message:
          'All built-in codecs were uninstalled and backed-up handlers have been restored.',
        scopeNotice: null,
        state: { ...mockState.codecs },
      } as T;

    /* ---- Optimizer ---- */
    case 'optimizer.getState':
      return { ...mockState.optimizer } as T;

    case 'optimizer.getRecommended':
      return {
        enforceOnStartup: true,
        preventThumbCacheCleanup: true,
        clearDiskCleanupFlags: true,
        disableSilentCleanupTask: false,
        disableNetworkThumbsDb: true,
        forceThumbnailPreviews: true,
        disableFolderTypeDiscovery: true,
        expandBagMruSize: true,
        expandIconCache: true,
        iconCacheSizeKb: 8192,
      } satisfies OptimizerProfileDto as T;

    case 'optimizer.apply': {
      const profile = (p.profile ?? mockState.optimizer.profile) as OptimizerProfileDto;
      const elevateViaUac = Boolean(p.elevateViaUac);
      const adminOnlyIds = new Set([
        'preventThumbCacheCleanup',
        'clearDiskCleanupFlags',
        'disableSilentCleanupTask',
        'expandIconCache',
      ]);
      const appliedStates: Record<
        string,
        'Default' | 'PartiallyOptimized' | 'Optimized'
      > = {};
      (Object.keys(profile) as Array<keyof OptimizerProfileDto>).forEach((k) => {
        if (k === 'iconCacheSizeKb') return;
        const on = Boolean(profile[k]);
        if (!on) {
          appliedStates[k] = 'Default';
        } else if (adminOnlyIds.has(k) && !elevateViaUac && !mockState.optimizer.isElevated) {
          appliedStates[k] = 'PartiallyOptimized';
        } else {
          appliedStates[k] = 'Optimized';
        }
      });

      const needsElev =
        !elevateViaUac &&
        !mockState.optimizer.isElevated &&
        (profile.preventThumbCacheCleanup ||
          profile.clearDiskCleanupFlags ||
          profile.disableSilentCleanupTask ||
          profile.expandIconCache);

      mockState.optimizer = {
        ...mockState.optimizer,
        needsElevationForProfile: needsElev,
        configuredIconCacheSizeKb: profile.iconCacheSizeKb,
        currentIconCacheSizeKb: profile.expandIconCache
          ? profile.iconCacheSizeKb
          : mockState.optimizer.currentIconCacheSizeKb,
        enforceOnStartup: profile.enforceOnStartup,
        profile: { ...profile },
        tweaks: buildOptimizerTweaks(profile, appliedStates),
      };

      return {
        ok: true,
        requiresElevationForFullCoverage: needsElev,
        uacCancelled: false,
        message: elevateViaUac
          ? 'System-wide (HKLM & Scheduled Task) optimizations were applied with Administrator privileges.'
          : needsElev
            ? 'User-level (HKCU) optimizations applied. Some system-wide settings require Administrator elevation.'
            : 'All selected Windows optimizations have been applied successfully.',
        state: { ...mockState.optimizer },
      } as T;
    }

    case 'optimizer.restoreDefaults': {
      const emptyProfile: OptimizerProfileDto = {
        enforceOnStartup: false,
        preventThumbCacheCleanup: false,
        clearDiskCleanupFlags: false,
        disableSilentCleanupTask: false,
        disableNetworkThumbsDb: false,
        forceThumbnailPreviews: false,
        disableFolderTypeDiscovery: false,
        expandBagMruSize: false,
        expandIconCache: false,
        iconCacheSizeKb: 8192,
      };
      mockState.optimizer = {
        ...mockState.optimizer,
        needsElevationForProfile: false,
        currentIconCacheSizeKb: 500,
        configuredIconCacheSizeKb: 8192,
        enforceOnStartup: false,
        profile: emptyProfile,
        tweaks: buildOptimizerTweaks(emptyProfile, {}),
      };
      return {
        ok: true,
        requiresElevationForFullCoverage: false,
        uacCancelled: false,
        message: 'Windows default settings have been restored.',
        state: { ...mockState.optimizer },
      } as T;
    }

    case 'optimizer.registerContextMenu':
      mockState.optimizer = {
        ...mockState.optimizer,
        contextMenuRegistered: true,
      };
      return {
        ok: true,
        requiresElevationForFullCoverage: false,
        uacCancelled: false,
        message: 'WinThumbsPreloader right-click context menu has been registered.',
        state: { ...mockState.optimizer },
      } as T;

    case 'optimizer.unregisterContextMenu':
      mockState.optimizer = {
        ...mockState.optimizer,
        contextMenuRegistered: false,
      };
      return {
        ok: true,
        requiresElevationForFullCoverage: false,
        uacCancelled: false,
        message: 'WinThumbsPreloader right-click context menu has been removed.',
        state: { ...mockState.optimizer },
      } as T;

    case 'optimizer.rebuildIconCache':
      return {
        ok: true,
        requiresElevationForFullCoverage: false,
        uacCancelled: false,
        message:
          'Windows Icon Cache rebuild triggered (4 unlocked iconcache_*.db file(s) removed). Preloaded thumbcache_*.db files kept intact.',
        state: { ...mockState.optimizer },
      } as T;

    case 'optimizer.restartExplorer':
      return {
        ok: true,
        requiresElevationForFullCoverage: false,
        uacCancelled: false,
        message: 'Windows Explorer restarted.',
        state: { ...mockState.optimizer },
      } as T;

    /* ---- Settings ---- */
    case 'settings.getState':
      return { ...mockState.settings } as T;

    case 'settings.saveGeneral': {
      const req = p as unknown as GeneralSettingsSaveRequest;
      const sizes = (req.preloaderThumbnailSizes ?? '')
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean);
      if (sizes.length === 0) {
        return {
          ok: false,
          cancelled: false,
          requiresConfirmation: false,
          relaunchRequired: false,
          message: 'Select at least one requested thumbnail size.',
          state: { ...mockState.settings },
        } as T;
      }
      const nextAdmin = req.admin ?? mockState.settings.general.admin;
      const relaunchRequired =
        nextAdmin !== mockState.settings.general.isRunningAsAdmin;
      mockState.settings.general = {
        ...mockState.settings.general,
        admin: nextAdmin,
        multithreaded: Boolean(req.multithreaded),
        threadCount: Math.max(0, Math.min(512, Number(req.threadCount ?? 0))),
        preloaderThumbnailSizes: sizes.join(','),
        preloaderProcessPriority: req.preloaderProcessPriority || 'Below Normal',
        progressDialogUpdateSpeed: Math.max(
          100,
          Math.min(1000, Number(req.progressDialogUpdateSpeed ?? 250))
        ),
        waitAfterPreloading: Boolean(req.waitAfterPreloading),
        waitTimeAfterPreloading: Number(req.waitTimeAfterPreloading ?? 10),
        waitAfterPreloadingUnit: req.waitAfterPreloadingUnit || 'Seconds',
        waitAfterCacheBackup: Boolean(req.waitAfterCacheBackup),
        waitTimeAfterCacheBackup: Number(req.waitTimeAfterCacheBackup ?? 10),
        waitAfterCacheUnit: req.waitAfterCacheUnit || 'Seconds',
      };
      mockState.codecs.adminSettingEnabled = nextAdmin;
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired,
        message: relaunchRequired
          ? 'General settings saved. Relaunch required for privilege mode change.'
          : 'General settings saved.',
        state: { ...mockState.settings },
      } as T;
    }

    case 'settings.resetGeneral':
      mockState.settings.general = {
        ...mockState.settings.general,
        multithreaded: true,
        threadCount: 0,
        preloaderThumbnailSizes: '96,256',
        preloaderProcessPriority: 'Below Normal',
        progressDialogUpdateSpeed: 250,
        waitAfterPreloading: true,
        waitTimeAfterPreloading: 10,
        waitAfterPreloadingUnit: 'Seconds',
        waitAfterCacheBackup: true,
        waitTimeAfterCacheBackup: 10,
        waitAfterCacheUnit: 'Seconds',
      };
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'General settings reset to defaults.',
        state: { ...mockState.settings },
      } as T;

    case 'settings.requestAdminElevation':
      mockState.settings.general.admin = true;
      mockState.codecs.adminSettingEnabled = true;
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: true,
        message: 'Restarting with Administrator privileges...',
        state: { ...mockState.settings },
      } as T;

    case 'settings.organizeExtensions':
      return {
        text: organizeExtensionsMock(
          String(p.text ?? ''),
          String(p.method ?? 'Disabled')
        ),
      } as T;

    case 'settings.saveExtensions': {
      const req = p as unknown as ExtensionsSaveRequest;
      const formatting = req.extensionsAutoFormatting || 'Disabled';
      const formattedText = organizeExtensionsMock(
        req.extensionsText ?? '',
        formatting
      ).slice(0, 10_000);
      mockState.settings.extensions = {
        ...mockState.settings.extensions,
        preloadFolderIcons: Boolean(req.preloadFolderIcons),
        preloadAllFolders: Boolean(req.preloadAllFolders),
        extensionsAutoFormatting: formatting,
        extensionsText: formattedText,
      };
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'Extension settings saved.',
        state: { ...mockState.settings },
      } as T;
    }

    case 'settings.restoreDefaultExtensions':
      mockState.settings.extensions.extensionsText = organizeExtensionsMock(
        DEFAULT_EXTENSIONS_TEXT,
        mockState.settings.extensions.extensionsAutoFormatting
      );
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'Default extensions restored.',
        state: { ...mockState.settings },
      } as T;

    case 'settings.importExtensions':
      return {
        ok: true,
        cancelled: false,
        text: 'jpg, png, psd, webp, avif',
        message: 'Extensions imported.',
      } as T;

    case 'settings.importDroppedExtensions':
      return {
        ok: true,
        cancelled: false,
        text: 'cr2, dng, nef, raf, svg',
        message: 'Extensions imported.',
      } as T;

    case 'settings.exportExtensions':
      return {
        ok: true,
        cancelled: false,
        path: 'C:\\Users\\Demo\\Documents\\ThumbnailExtensions.txt',
        message: 'Extensions exported.',
      } as T;

    case 'settings.saveLogging': {
      const req = p as unknown as LoggingSaveRequest;
      const freq = Math.max(0, Math.min(4, Number(req.loggingFrequency ?? 0)));
      const path = (req.loggerFolderPath ?? '').trim();
      if (freq !== 0 && !path) {
        return {
          ok: false,
          cancelled: false,
          requiresConfirmation: false,
          relaunchRequired: false,
          message: 'Choose a log folder or disable logging before saving.',
          state: { ...mockState.settings },
        } as T;
      }
      const status = evaluateMockLogFolderStatus(path);
      if (status.status === 'WritableAdmin' && !req.confirmAdminWarning) {
        return {
          ok: false,
          cancelled: false,
          requiresConfirmation: true,
          relaunchRequired: false,
          message:
            'Logging may fail when WinThumbsPreloader is launched normally without administrator privileges. Use this folder anyway?',
          state: { ...mockState.settings },
        } as T;
      }
      mockState.settings.logging = {
        ...mockState.settings.logging,
        loggingFrequency: freq,
        loggerFolderPath: path,
        logFolderStatus: status,
        logsSizeBytes: path ? 42_500 : 0,
        logsSizeFormatted: path ? '41.5 KB' : 'N/A',
        autoDeleteLogsByAge: Boolean(req.autoDeleteLogsByAge),
        logRetentionDays: Math.max(
          1,
          Math.min(3650, Number(req.logRetentionDays ?? 30))
        ),
        maxLogFileSizeMB: Math.max(
          1,
          Math.min(10240, Number(req.maxLogFileSizeMB ?? 1024))
        ),
      };
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'Log settings saved.',
        state: { ...mockState.settings },
      } as T;
    }

    case 'settings.resetLogging':
      mockState.settings.logging = {
        ...mockState.settings.logging,
        loggingFrequency: 0,
        autoDeleteLogsByAge: false,
        logRetentionDays: 30,
        maxLogFileSizeMB: 1024,
      };
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'Log defaults saved.',
        state: { ...mockState.settings },
      } as T;

    case 'settings.pickLoggerFolder': {
      const path = mockState.settings.logging.recommendedDefaultLogPath;
      return {
        cancelled: false,
        path,
        logFolderStatus: evaluateMockLogFolderStatus(path),
      } as T;
    }

    case 'settings.getLogFolderStatus':
      return evaluateMockLogFolderStatus(String(p.path ?? '')) as T;

    case 'settings.openLogsFolder':
      if (!mockState.settings.logging.loggerFolderPath) {
        return {
          ok: false,
          cancelled: false,
          requiresConfirmation: false,
          relaunchRequired: false,
          message: 'Select and save a log folder before opening logs.',
          state: null,
        } as T;
      }
      return {
        ok: true,
        cancelled: false,
        requiresConfirmation: false,
        relaunchRequired: false,
        message: 'Logs folder opened.',
        state: null,
      } as T;

    case 'settings.clearLogs':
      mockState.settings.logging.logsSizeBytes = 0;
      mockState.settings.logging.logsSizeFormatted = mockState.settings.logging
        .loggerFolderPath
        ? '0.00 KB'
        : 'N/A';
      return {
        ok: true,
        deletedCount: 3,
        failedCount: 0,
        message: 'Cleared 3 log file(s).',
        logsSizeBytes: mockState.settings.logging.logsSizeBytes,
        logsSizeFormatted: mockState.settings.logging.logsSizeFormatted,
      } as T;

    default:
      throw new Error(`Unsupported mock IPC command: ${cmd}`);
  }
}
