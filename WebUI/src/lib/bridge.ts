import type {
  AdvancedCacheSettingsRequest,
  BootstrapData,
  BulkPreset,
  CacheFolderPickResult,
  CacheOperationSummary,
  CacheSnapshot,
  ClearLogsResult,
  CodecsActionResult,
  CodecsStateDto,
  DragDropResolveResult,
  ExtensionsImportResult,
  ExtensionsSaveRequest,
  GeneralSettingsSaveRequest,
  LogFolderStatusDto,
  LoggerFolderPickResult,
  LoggingSaveRequest,
  OkResult,
  OpenUrlResult,
  OptimizerActionResult,
  OptimizerProfileDto,
  OptimizerStateDto,
  PreloadExportResult,
  PreloadFolderPickResult,
  PreloadInitialState,
  PreloadPresetsResult,
  PreloadRunRequest,
  PreloadStartResult,
  PreloadValidateResult,
  ScheduleActionResult,
  ScheduleSaveRequest,
  ScheduleState,
  SetBackupFolderResult,
  SetThemeResult,
  SettingsActionResult,
  SettingsStateDto,
  ThemeMode,
  UpdateInstallResult,
  UpdateStatusDto,
} from './types';
import { emitMockEvent, handleMockCommand } from './mockBridge';

export const TRUSTED_ORIGIN = 'https://app.winthumbspreloader.local';
const REQUEST_TIMEOUT_MS = 30_000;

interface WebView2Bridge {
  postMessage: (message: unknown) => void;
  postMessageWithAdditionalObjects?: (message: unknown, additionalObjects: unknown[]) => void;
  addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void;
  removeEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: WebView2Bridge;
    };
  }
}

interface OriginLike {
  isSameOrigin(other: OriginLike): boolean;
}

interface OriginStatic {
  from(value: unknown): OriginLike;
}

function getGlobalOriginCtor(): OriginStatic | undefined {
  const ctor = (globalThis as { Origin?: unknown }).Origin;
  if (
    typeof ctor === 'function' &&
    typeof (ctor as { from?: unknown }).from === 'function'
  ) {
    return (ctor as unknown) as OriginStatic;
  }
  return undefined;
}

function extractUrlString(candidate: unknown): string {
  if (typeof candidate === 'string') return candidate;
  if (typeof MessageEvent !== 'undefined' && candidate instanceof MessageEvent) {
    return candidate.origin;
  }
  if (candidate && typeof candidate === 'object' && 'href' in candidate) {
    const href = (candidate as { href?: unknown }).href;
    return typeof href === 'string' ? href : '';
  }
  return '';
}

function extractOrigin(originCtor: OriginStatic, candidate: unknown): OriginLike {
  try {
    return originCtor.from(candidate);
  } catch (err) {
    if (typeof MessageEvent !== 'undefined' && candidate instanceof MessageEvent) {
      return originCtor.from(candidate.origin);
    }
    throw err;
  }
}

/**
 * Validates incoming message/URL origins using Origin.from() with URL fallback,
 * explicitly rejecting opaque "null" origins and untrusted schemes/hosts.
 */
export function isTrustedMessageOrigin(
  candidate: unknown,
  trustedUrl: string = TRUSTED_ORIGIN
): boolean {
  try {
    const originCtor = getGlobalOriginCtor();
    if (originCtor) {
      return extractOrigin(originCtor, candidate).isSameOrigin(
        originCtor.from(trustedUrl)
      );
    }
    const candidateUrl = new URL(extractUrlString(candidate));
    const targetUrl = new URL(trustedUrl);
    return (
      candidateUrl.origin !== 'null' &&
      candidateUrl.origin === targetUrl.origin
    );
  } catch {
    return false;
  }
}

function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `req-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
  timer: ReturnType<typeof setTimeout>;
}

const pending = new Map<string, PendingRequest>();
const subscribers = new Map<string, Set<(payload: unknown) => void>>();
let listenerAttached = false;

function handleWebViewMessage(event: MessageEvent): void {
  if (typeof event.origin === 'string' && event.origin.length > 0) {
    if (!isTrustedMessageOrigin(event)) {
      return;
    }
  }

  let msg: unknown = event.data;
  if (typeof msg === 'string') {
    try {
      msg = JSON.parse(msg);
    } catch {
      return;
    }
  }

  if (!msg || typeof msg !== 'object') {
    return;
  }

  const record = msg as Record<string, unknown>;

  if (typeof record.event === 'string') {
    const handlers = subscribers.get(record.event);
    if (handlers) {
      for (const handler of handlers) {
        try {
          handler(record.payload);
        } catch {
          // Keep event fan-out isolated per subscriber.
        }
      }
    }
    return;
  }

  if (typeof record.id === 'string') {
    const req = pending.get(record.id);
    if (!req) return;
    pending.delete(record.id);
    clearTimeout(req.timer);

    if (record.ok === true) {
      req.resolve(record.data);
    } else {
      const errObj = record.error as { code?: string; message?: string } | undefined;
      const errMsg = errObj?.message || 'IPC command failed.';
      req.reject(new Error(errMsg));
    }
  }
}

function ensureWebViewListener(): WebView2Bridge | null {
  const webview = typeof window !== 'undefined' ? window.chrome?.webview : undefined;
  if (!webview) {
    return null;
  }
  if (!listenerAttached) {
    webview.addEventListener('message', handleWebViewMessage);
    listenerAttached = true;
  }
  return webview;
}

export function isWebView2Host(): boolean {
  return typeof window !== 'undefined' && Boolean(window.chrome?.webview);
}

export async function invoke<T = unknown>(
  cmd: string,
  payload: Record<string, unknown> | object | null = null
): Promise<T> {
  const webview = ensureWebViewListener();
  if (!webview) {
    return handleMockCommand<T>(cmd, payload);
  }

  const id = createCorrelationId();
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`IPC timeout after ${REQUEST_TIMEOUT_MS}ms for '${cmd}'.`));
    }, REQUEST_TIMEOUT_MS);

    pending.set(id, {
      resolve: (data) => resolve(data as T),
      reject,
      timer,
    });

    try {
      webview.postMessage({ id, cmd, payload });
    } catch (err) {
      pending.delete(id);
      clearTimeout(timer);
      reject(err instanceof Error ? err : new Error(String(err)));
    }
  });
}

export async function invokeWithFiles<T = unknown>(
  cmd: string,
  payload: Record<string, unknown> | object | null,
  files: File[]
): Promise<T> {
  const webview = ensureWebViewListener();
  if (!webview) {
    const filePaths = files.map((f) => {
      const withPath = f as File & { path?: string };
      return withPath.path || f.name;
    });
    const mergedPayload = {
      ...(payload && typeof payload === 'object' ? payload : {}),
      paths: [
        ...((payload as { paths?: string[] } | null)?.paths ?? []),
        ...filePaths,
      ],
    };
    return handleMockCommand<T>(cmd, mergedPayload);
  }

  const postFiles = webview.postMessageWithAdditionalObjects;
  if (typeof postFiles !== 'function') {
    return invoke<T>(cmd, payload);
  }

  const id = createCorrelationId();
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`IPC file timeout after ${REQUEST_TIMEOUT_MS}ms for '${cmd}'.`));
    }, REQUEST_TIMEOUT_MS);

    pending.set(id, {
      resolve: (data) => resolve(data as T),
      reject,
      timer,
    });

    try {
      postFiles.call(webview, { id, cmd, payload }, files);
    } catch (err) {
      pending.delete(id);
      clearTimeout(timer);
      reject(err instanceof Error ? err : new Error(String(err)));
    }
  });
}

export function subscribe<T = unknown>(
  eventName: string,
  handler: (payload: T) => void
): () => void {
  ensureWebViewListener();

  let set = subscribers.get(eventName);
  if (!set) {
    set = new Set();
    subscribers.set(eventName, set);
  }

  const targetSet = set;
  const wrapped = (payload: unknown) => handler(payload as T);
  targetSet.add(wrapped);

  return () => {
    targetSet.delete(wrapped);
    if (targetSet.size === 0) {
      subscribers.delete(eventName);
    }
  };
}

export function dispatchSyntheticBridgeEvent(eventName: string, payload: unknown): void {
  const handlers = subscribers.get(eventName);
  if (handlers) {
    for (const handler of handlers) {
      handler(payload);
    }
  }
  emitMockEvent(eventName, payload);
}

/* Typed convenience IPC methods matching WebBridge.cs */
export const bridge = {
  getBootstrap: () => invoke<BootstrapData>('app.getBootstrap'),
  setTheme: (theme: ThemeMode) => invoke<SetThemeResult>('app.setTheme', { theme }),
  minimizeToTray: () => invoke<OkResult>('app.minimizeToTray'),
  quit: () => invoke<OkResult>('app.quit'),
  openExternalUrl: (url: string) => invoke<OpenUrlResult>('app.openExternalUrl', { url }),
  checkForUpdates: () => invoke<UpdateStatusDto>('app.checkForUpdates'),
  downloadAndInstallUpdate: (restartMinimized = false) =>
    invoke<UpdateInstallResult>('app.downloadAndInstallUpdate', { restartMinimized }),

  resolveDrop: (paths: string[], files: File[] = []) =>
    files.length > 0
      ? invokeWithFiles<DragDropResolveResult>('dragDrop.resolve', { paths }, files)
      : invoke<DragDropResolveResult>('dragDrop.resolve', { paths }),

  preload: {
    getState: () => invoke<PreloadInitialState>('preload.getState'),
    pickFolder: (multiSelect = true) =>
      invoke<PreloadFolderPickResult>('preload.pickFolder', { multiSelect }),
    validatePaths: (paths: string[]) =>
      invoke<PreloadValidateResult>('preload.validatePaths', { paths }),
    start: (req: PreloadRunRequest) =>
      invoke<PreloadStartResult>('preload.start', req),
    savePreset: (preset: BulkPreset) =>
      invoke<PreloadPresetsResult>('preload.savePreset', preset),
    deletePreset: (name: string) =>
      invoke<PreloadPresetsResult>('preload.deletePreset', { name }),
    exportPaths: (paths: string[]) =>
      invoke<PreloadExportResult>('preload.exportPaths', { paths }),
    exportPresets: () => invoke<PreloadExportResult>('preload.exportPresets'),
  },

  cache: {
    getState: () => invoke<CacheSnapshot>('cache.getState'),
    backup: () => invoke<CacheOperationSummary>('cache.backup'),
    restore: (force = false) => invoke<CacheOperationSummary>('cache.restore', { force }),
    clearBackup: () => invoke<CacheOperationSummary>('cache.clearBackup'),
    clearCache: () => invoke<CacheOperationSummary>('cache.clearCache'),
    pickBackupFolder: () => invoke<CacheFolderPickResult>('cache.pickBackupFolder'),
    setBackupFolder: (path: string | null, confirmAdminWarning = false) =>
      invoke<SetBackupFolderResult>('cache.setBackupFolder', {
        path,
        confirmAdminWarning,
      }),
    openCacheFolder: () => invoke<CacheOperationSummary>('cache.openCacheFolder'),
    openBackupFolder: () => invoke<CacheOperationSummary>('cache.openBackupFolder'),
    setStartWithWindows: (enabled: boolean) =>
      invoke<CacheOperationSummary>('cache.setStartWithWindows', { enabled }),
    setAutoFlags: (flags: {
      autoBackup?: boolean | null;
      autoRestore?: boolean | null;
      alertOnReset?: boolean | null;
    }) => invoke<CacheOperationSummary>('cache.setAutoFlags', flags),
    saveAdvanced: (req: AdvancedCacheSettingsRequest) =>
      invoke<CacheOperationSummary>('cache.saveAdvanced', req),
    resetAdvanced: (scope: 'all' | 'general' | 'cache' | 'backup' = 'all') =>
      invoke<CacheOperationSummary>('cache.resetAdvanced', { scope }),
  },

  schedule: {
    getState: () => invoke<ScheduleState>('schedule.getState'),
    setEnabled: (enabled: boolean) =>
      invoke<ScheduleActionResult>('schedule.setEnabled', { enabled }),
    saveTask: (req: ScheduleSaveRequest) =>
      invoke<ScheduleActionResult>('schedule.saveTask', req),
    deleteTask: () => invoke<ScheduleActionResult>('schedule.deleteTask'),
    savePreferences: (prefs: {
      driveSelectionEnabled: boolean;
      driveSortChoice: 'Vertical' | 'Horizontal';
      twentyFourHourMode: boolean;
      detailedTimeMode: boolean;
    }) => invoke<ScheduleActionResult>('schedule.savePreferences', prefs),
    resetPreferences: () =>
      invoke<ScheduleActionResult>('schedule.resetPreferences'),
    openTaskScheduler: () =>
      invoke<ScheduleActionResult>('schedule.openTaskScheduler'),
  },

  codecs: {
    getState: () => invoke<CodecsStateDto>('codecs.getState'),
    apply: (mask: number, elevateViaUac: boolean, syncExtensions: boolean) =>
      invoke<CodecsActionResult>('codecs.apply', {
        mask,
        elevateViaUac,
        syncExtensions,
      }),
    uninstallAll: () => invoke<CodecsActionResult>('codecs.uninstallAll'),
  },

  optimizer: {
    getState: () => invoke<OptimizerStateDto>('optimizer.getState'),
    getRecommended: () => invoke<OptimizerProfileDto>('optimizer.getRecommended'),
    apply: (profile: OptimizerProfileDto, elevateViaUac: boolean) =>
      invoke<OptimizerActionResult>('optimizer.apply', {
        profile,
        elevateViaUac,
      }),
    restoreDefaults: () =>
      invoke<OptimizerActionResult>('optimizer.restoreDefaults'),
    registerContextMenu: () =>
      invoke<OptimizerActionResult>('optimizer.registerContextMenu'),
    unregisterContextMenu: () =>
      invoke<OptimizerActionResult>('optimizer.unregisterContextMenu'),
    rebuildIconCache: () =>
      invoke<OptimizerActionResult>('optimizer.rebuildIconCache'),
    restartExplorer: () =>
      invoke<OptimizerActionResult>('optimizer.restartExplorer'),
  },

  settings: {
    getState: () => invoke<SettingsStateDto>('settings.getState'),
    saveGeneral: (req: GeneralSettingsSaveRequest) =>
      invoke<SettingsActionResult>('settings.saveGeneral', req),
    resetGeneral: () =>
      invoke<SettingsActionResult>('settings.resetGeneral'),
    requestAdminElevation: () =>
      invoke<SettingsActionResult>('settings.requestAdminElevation'),
    organizeExtensions: (text: string, method: string) =>
      invoke<{ text: string }>('settings.organizeExtensions', { text, method }),
    saveExtensions: (req: ExtensionsSaveRequest) =>
      invoke<SettingsActionResult>('settings.saveExtensions', req),
    restoreDefaultExtensions: () =>
      invoke<SettingsActionResult>('settings.restoreDefaultExtensions'),
    importExtensions: () =>
      invoke<ExtensionsImportResult>('settings.importExtensions'),
    importDroppedExtensions: (files: File[]) =>
      invokeWithFiles<ExtensionsImportResult>(
        'settings.importDroppedExtensions',
        null,
        files
      ),
    exportExtensions: (text: string) =>
      invoke<PreloadExportResult>('settings.exportExtensions', { text }),
    saveLogging: (req: LoggingSaveRequest) =>
      invoke<SettingsActionResult>('settings.saveLogging', req),
    resetLogging: () =>
      invoke<SettingsActionResult>('settings.resetLogging'),
    pickLoggerFolder: () =>
      invoke<LoggerFolderPickResult>('settings.pickLoggerFolder'),
    getLogFolderStatus: (path: string) =>
      invoke<LogFolderStatusDto>('settings.getLogFolderStatus', { path }),
    openLogsFolder: () =>
      invoke<SettingsActionResult>('settings.openLogsFolder'),
    clearLogs: () => invoke<ClearLogsResult>('settings.clearLogs'),
  },
};
