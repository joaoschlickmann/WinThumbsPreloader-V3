export type ViewId =
  | 'preload'
  | 'cache'
  | 'schedule'
  | 'codecs'
  | 'optimizer'
  | 'settings';

export type ThemeMode = 'dark' | 'light' | 'system';

export interface BootstrapData {
  appName: string;
  version: string;
  isRunningAsAdmin: boolean;
  theme: ThemeMode;
  effectiveDarkMode: boolean;
  initialView: ViewId;
  isWebView2: boolean;
  invalidLaunchPathWarning: string | null;
}

export interface SetThemeResult {
  ok: boolean;
  theme: ThemeMode;
  effectiveDarkMode: boolean;
}

export interface OkResult {
  ok: boolean;
}

export interface OpenUrlResult {
  ok: boolean;
  url: string;
}

export interface UpdateStatusDto {
  state: 'Updated' | 'UpdateAvailable' | 'BetaAvailable' | 'Error' | string;
  currentVersion: string;
  latestVersion: string;
  releaseTag: string;
  releasePageUrl: string;
  hasInstallerAsset: boolean;
  installerFileName: string;
  installerSize: number;
  isUpdateAvailable: boolean;
}

export interface UpdateInstallResult {
  ok: boolean;
  launched: boolean;
  cancelledByUser: boolean;
  openedReleasePageFallback: boolean;
  message: string;
  releasePageUrl: string | null;
}

export interface UpdateProgressEvent {
  percent: number;
  status: string;
}

/* ---- Preload ---- */

export interface BulkPreset {
  name: string;
  paths: string[];
  recursive: boolean;
  multiThreaded: boolean;
  threads: number;
  maximumInstances: number;
  method: 'Sequentially' | 'Simultaneously';
}

export interface PreloadInitialState {
  defaultMultithreaded: boolean;
  defaultThreadCount: number;
  savedPresets: BulkPreset[];
  invalidLaunchPathWarning: string | null;
}

export interface PreloadPathValidationItem {
  input: string;
  normalizedPath: string | null;
  valid: boolean;
  isNetwork: boolean;
}

export interface PreloadValidateResult {
  items: PreloadPathValidationItem[];
}

export interface DragDropResolveResult {
  items: PreloadPathValidationItem[];
  paths: string[];
}

export interface PreloadFolderPickResult {
  cancelled: boolean;
  path: string | null;
  paths: string[];
}

export interface PreloadRunRequest {
  paths: string[];
  recursive: boolean;
  multithreaded: boolean;
  threads: number;
  method: 'Sequentially' | 'Simultaneously';
  maximumInstances: number;
  skipInvalid?: boolean;
}

export interface PreloadStartResult {
  ok: boolean;
  requiresConfirmation: boolean;
  message: string;
  launchedPaths: string[];
  invalidPaths: string[];
}

export interface PreloadPresetsResult {
  ok: boolean;
  message: string;
  savedPresets: BulkPreset[];
}

export interface PreloadExportResult {
  ok: boolean;
  cancelled: boolean;
  path: string;
  message: string;
}

/* ---- Cache ---- */

export interface CacheBucketInfo {
  size: string;
  sizeBytes: number;
  softLimitBytes: number;
  saturated: boolean;
}

export type BackupFolderStatusKind =
  | 'NotSelected'
  | 'Writable'
  | 'WritableAdmin'
  | 'WillBeCreated'
  | 'WillBeCreatedAdmin'
  | 'NotWritable'
  | 'InvalidPath'
  | string;

export interface BackupFolderStatusDto {
  status: BackupFolderStatusKind;
  message: string;
  normalizedPath: string;
  isRunningAsAdmin: boolean;
  canUseForBackup: boolean;
}

export type BackupRestoreSafetyMode = 'Allow Partial Copy' | 'Require Full Copy';
export type ExplorerDeletionMethod = 'Manual Deletion' | 'Disk Cleanup';
export type ExplorerCloseFrequency = 'On Every File' | 'On Deletion Start';
export type CacheSizeFormat = 'Auto' | 'KB' | 'MB' | 'GB';

export interface CacheSnapshot {
  explorerSizeBytes: number;
  explorerSizeFormatted: string;
  backupSizeBytesNullable: number | null;
  backupSizeFormatted: string;
  backupFolderPath: string;
  backupFolderStatus: BackupFolderStatusDto;
  recommendedDefaultBackupPath: string;
  lastBackupDateTimeIso: string | null;
  resetRecognized: boolean;
  backupLargerThanExplorer: boolean;
  lockHeld: boolean;
  startWithWindows: boolean;
  autoBackup: boolean;
  autoRestore: boolean;
  alertOnReset: boolean;
  autoBackupAfterPreload: boolean;
  backupRestoreClearSafety: BackupRestoreSafetyMode;
  explorerCacheDeletionMethod: ExplorerDeletionMethod;
  explorerCloseFrequency: ExplorerCloseFrequency;
  cacheSizeFormat: CacheSizeFormat;
  cacheUpdateIntervalMs: number;
  autoBackupIntervalMs: number;
  autoRestoreIntervalMs: number;
  buckets: CacheBucketInfo[];
}

export interface SetBackupFolderResult {
  ok: boolean;
  status: string;
  message: string;
  path: string;
  disabledAutoToggles: boolean;
  requiresConfirmation: boolean;
}

export interface CacheOperationSummary {
  ok: boolean;
  status: string;
  message: string;
  failureReason: string;
  snapshot: CacheSnapshot | null;
}

export interface CacheFolderPickResult {
  cancelled: boolean;
  path: string;
  status: BackupFolderStatusDto | null;
}

export interface AdvancedCacheSettingsRequest {
  autoBackupAfterPreload?: boolean;
  explorerCacheDeletionMethod?: ExplorerDeletionMethod;
  explorerCloseFrequency?: ExplorerCloseFrequency;
  backupRestoreClearSafety?: BackupRestoreSafetyMode;
  cacheSizeFormat?: CacheSizeFormat;
  cacheUpdateIntervalMs?: number;
  autoBackupIntervalMs?: number;
  autoRestoreIntervalMs?: number;
}

export interface CacheProgressEvent {
  operation: 'backup' | 'restore' | string;
  percent: number;
  done?: boolean;
}

export interface CacheResetEvent {
  explorerSizeBytes: number;
  backupSizeBytes: number;
  autoRestored: boolean;
  restoreSucceeded: boolean;
  message: string;
}

/* ---- Schedule ---- */

export type ScheduleFrequency = 'Hour' | 'Day' | 'Week' | 'Month' | '';
export type DriveSortChoice = 'Vertical' | 'Horizontal';

export interface ScheduleDriveInfo {
  letter: string;
  root: string;
  isMounted: boolean;
  driveType: string;
  label: string;
}

export interface ScheduleState {
  taskExists: boolean;
  enabled: boolean;
  frequency: ScheduleFrequency;
  hour: number;
  minute: number;
  onIdle: boolean;
  multithreaded: boolean;
  threads: number;
  drives: string[];
  customPaths: string[];
  lastRunTimeText: string;
  nextRunTimeText: string;
  statusMessage: string;
  driveSelectionEnabled: boolean;
  driveSortChoice: DriveSortChoice;
  twentyFourHourMode: boolean;
  detailedTimeMode: boolean;
  availableDrives: ScheduleDriveInfo[];
}

export interface ScheduleSaveRequest {
  frequency: 'Hour' | 'Day' | 'Week' | 'Month';
  hour: number;
  minute: number;
  onIdle: boolean;
  enabled: boolean;
  multithreaded: boolean;
  threads: number;
  drives: string[];
  customPaths: string[];
  description?: string | null;
}

export interface ScheduleActionResult {
  ok: boolean;
  message: string;
  state: ScheduleState | null;
}

/* ---- Codecs (Phase 3 contracts) ---- */

export interface CodecFamilyDto {
  bitIndex: number;
  id: string;
  displayName: string;
  description: string;
  extensions: string[];
  currentHandlerName: string;
  statusText: string;
  isEnabled: boolean;
  isPartiallyEnabled: boolean;
  hasExternalHandler: boolean;
  recommended: boolean;
}

export interface CodecsStateDto {
  bundlePresent: boolean;
  bundleError: string | null;
  isElevated: boolean;
  adminSettingEnabled: boolean;
  enabledMask: number;
  hasMachineLevelCodecsOrBackup: boolean;
  families: CodecFamilyDto[];
}

export interface CodecsActionResult {
  ok: boolean;
  uacCancelled: boolean;
  machineUninstallSkipped: boolean;
  extensionsSynced: boolean;
  appliedMask: number;
  message: string;
  scopeNotice: string | null;
  state: CodecsStateDto | null;
}

/* ---- Optimizer (Phase 3 contracts) ---- */

export interface OptimizerTweakDto {
  id: string;
  title: string;
  tab: 'cacheNetwork' | 'explorer' | 'shell' | string;
  requiresAdmin: boolean;
  enabled: boolean;
  state: string;
  whatItDoes: string;
  pros: string;
  cons: string;
}

export interface OptimizerProfileDto {
  enforceOnStartup: boolean;
  preventThumbCacheCleanup: boolean;
  clearDiskCleanupFlags: boolean;
  disableSilentCleanupTask: boolean;
  disableNetworkThumbsDb: boolean;
  forceThumbnailPreviews: boolean;
  disableFolderTypeDiscovery: boolean;
  expandBagMruSize: boolean;
  expandIconCache: boolean;
  iconCacheSizeKb: number;
}

export interface OptimizerShellToolHelpDto {
  id: string;
  title: string;
  whatItDoes: string;
  pros: string;
  cons: string;
}

export interface OptimizerStateDto {
  isElevated: boolean;
  needsElevationForProfile: boolean;
  contextMenuRegistered: boolean;
  contextMenuSystemWide: boolean;
  currentIconCacheSizeKb: number;
  configuredIconCacheSizeKb: number;
  enforceOnStartup: boolean;
  profile: OptimizerProfileDto;
  tweaks: OptimizerTweakDto[];
  shellTools: OptimizerShellToolHelpDto[];
}

export interface OptimizerActionResult {
  ok: boolean;
  requiresElevationForFullCoverage: boolean;
  uacCancelled: boolean;
  message: string;
  state: OptimizerStateDto | null;
}

/* ---- Settings (Phase 3 contracts) ---- */

export interface GeneralSettingsDto {
  admin: boolean;
  isRunningAsAdmin: boolean;
  multithreaded: boolean;
  threadCount: number;
  preloaderThumbnailSizes: string;
  preloaderProcessPriority: string;
  progressDialogUpdateSpeed: number;
  waitAfterPreloading: boolean;
  waitTimeAfterPreloading: number;
  waitAfterPreloadingUnit: 'Seconds' | 'Minutes' | 'Hours' | string;
  waitAfterCacheBackup: boolean;
  waitTimeAfterCacheBackup: number;
  waitAfterCacheUnit: 'Seconds' | 'Minutes' | 'Hours' | string;
}

export interface ExtensionsSettingsDto {
  preloadFolderIcons: boolean;
  preloadAllFolders: boolean;
  extensionsAutoFormatting: string;
  extensionsText: string;
  defaultExtensionsText: string;
}

export interface LogFolderStatusDto {
  status: string;
  message: string;
  normalizedPath: string;
  isRunningAsAdmin: boolean;
  usesDefaultPath: boolean;
  canUseForLogging: boolean;
}

export interface LoggingSettingsDto {
  loggingFrequency: number;
  loggerFolderPath: string;
  recommendedDefaultLogPath: string;
  logFolderStatus: LogFolderStatusDto;
  logsSizeBytes: number;
  logsSizeFormatted: string;
  autoDeleteLogsByAge: boolean;
  logRetentionDays: number;
  maxLogFileSizeMB: number;
}

export interface AboutInfoDto {
  appName: string;
  version: string;
  isRunningAsAdmin: boolean;
  projectUrl: string;
  licenseText: string;
}

export interface SettingsStateDto {
  general: GeneralSettingsDto;
  extensions: ExtensionsSettingsDto;
  logging: LoggingSettingsDto;
  about: AboutInfoDto;
}

export interface SettingsActionResult {
  ok: boolean;
  cancelled: boolean;
  requiresConfirmation: boolean;
  relaunchRequired: boolean;
  message: string;
  state: SettingsStateDto | null;
}

export interface GeneralSettingsSaveRequest {
  admin?: boolean;
  multithreaded: boolean;
  threadCount: number;
  preloaderThumbnailSizes: string;
  preloaderProcessPriority: string;
  progressDialogUpdateSpeed: number;
  waitAfterPreloading: boolean;
  waitTimeAfterPreloading: number;
  waitAfterPreloadingUnit: string;
  waitAfterCacheBackup: boolean;
  waitTimeAfterCacheBackup: number;
  waitAfterCacheUnit: string;
}

export interface ExtensionsSaveRequest {
  preloadFolderIcons: boolean;
  preloadAllFolders: boolean;
  extensionsAutoFormatting: string;
  extensionsText: string;
}

export interface LoggingSaveRequest {
  loggingFrequency: number;
  loggerFolderPath: string | null;
  autoDeleteLogsByAge: boolean;
  logRetentionDays: number;
  maxLogFileSizeMB?: number;
  confirmAdminWarning: boolean;
}

export interface ExtensionsImportResult {
  ok: boolean;
  cancelled: boolean;
  text: string;
  message: string;
}

export interface LoggerFolderPickResult {
  cancelled: boolean;
  path: string;
  logFolderStatus: LogFolderStatusDto | null;
}

export interface ClearLogsResult {
  ok: boolean;
  deletedCount: number;
  failedCount: number;
  message: string;
  logsSizeBytes: number;
  logsSizeFormatted: string;
}

/* ---- Events ---- */

export interface BridgeEventMap {
  'shell:navigate': { view: ViewId };
  'cache:telemetry': CacheSnapshot;
  'cache:progress': CacheProgressEvent;
  'cache:resetDetected': CacheResetEvent;
  'update:progress': UpdateProgressEvent;
  'update:available': UpdateStatusDto;
}

export type BridgeEventName = keyof BridgeEventMap;
