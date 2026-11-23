import type {
  CodecFamilyDto,
  CodecsStateDto,
  OptimizerProfileDto,
  OptimizerShellToolHelpDto,
  OptimizerStateDto,
  OptimizerTweakDto,
  SettingsStateDto,
} from './types';

export const DEFAULT_EXTENSIONS_TEXT =
  'ai, avi, avif, bmp, gif, heic, heif, jpg, jpeg, mkv, mov, mp4, png, psb, psd, svg, tif, tiff, webp';

export function buildInitialCodecsState(): CodecsStateDto {
  const families: CodecFamilyDto[] = [
    {
      bitIndex: 0,
      id: 'PsdPsb',
      displayName: 'Adobe Photoshop (PSD, PSB)',
      description:
        'Embedded 1036/1033 preview + lazy PSB row-skipping + ICC CMYK/RGB/Lab',
      extensions: ['.psd', '.psb'],
      currentHandlerName: 'WinThumbsCodecs',
      statusText: 'Active',
      isEnabled: true,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 1,
      id: 'Pdf',
      displayName: 'PDF Documents (PDFium)',
      description: 'High-fidelity CMYK & transparency rendering via PDFium',
      extensions: ['.pdf'],
      currentHandlerName: 'Microsoft Edge / PDF Handler',
      statusText: 'External',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: true,
      recommended: true,
    },
    {
      bitIndex: 2,
      id: 'Illustrator',
      displayName: 'Adobe Illustrator (AI)',
      description:
        'PDF-compatible AI via PDFium + legacy PostScript AI fallback',
      extensions: ['.ai'],
      currentHandlerName: 'None',
      statusText: 'Not Installed',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 3,
      id: 'Eps',
      displayName: 'Encapsulated PostScript (EPS)',
      description:
        'DOS TIFF/WMF + EPSI + clean-room PostScript Level 3 fallback',
      extensions: ['.eps'],
      currentHandlerName: 'None',
      statusText: 'Not Installed',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 4,
      id: 'InDesign',
      displayName: 'Adobe InDesign (INDD, IDML)',
      description:
        'Streaming XMP page preview extraction + IDML archive preview',
      extensions: ['.indd', '.idml'],
      currentHandlerName: 'None',
      statusText: 'Not Installed',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 5,
      id: 'AdobeXd',
      displayName: 'Adobe XD (XD)',
      description: 'ZIP thumbnail.png extraction with zip-bomb cap',
      extensions: ['.xd'],
      currentHandlerName: 'None',
      statusText: 'Not Installed',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 6,
      id: 'Svg',
      displayName: 'SVG & SVGZ Vector Graphics',
      description: 'Fast resvg vector rasterizer (external URLs blocked)',
      extensions: ['.svg', '.svgz'],
      currentHandlerName: 'WinThumbsCodecs',
      statusText: 'Active',
      isEnabled: true,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 7,
      id: 'CorelDraw',
      displayName: 'CorelDRAW (CDR)',
      description:
        'RIFF DISP BMP & ZIP package thumbnail extraction (v4-v27)',
      extensions: ['.cdr'],
      currentHandlerName: 'None',
      statusText: 'Not Installed',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 8,
      id: 'Affinity',
      displayName: 'Affinity Suite (AF, AFPHOTO, AFDESIGN, AFPUB)',
      description: 'Native Affinity container Thmb PNG preview extraction',
      extensions: ['.afphoto', '.afdesign', '.afpub', '.af'],
      currentHandlerName: 'WinThumbsCodecs',
      statusText: 'Active',
      isEnabled: true,
      isPartiallyEnabled: false,
      hasExternalHandler: false,
      recommended: true,
    },
    {
      bitIndex: 9,
      id: 'CameraRaw',
      displayName: 'Camera RAW (DNG, CR2, CR3, NEF, ARW, RAF...)',
      description:
        'Embedded JPEG/TIFF/JXL preview + EXIF orientation across 20 RAW formats',
      extensions: [
        '.dng',
        '.cr2',
        '.cr3',
        '.crw',
        '.nef',
        '.nrw',
        '.arw',
        '.raf',
        '.orf',
        '.rw2',
        '.pef',
        '.srw',
        '.3fr',
        '.fff',
        '.mrw',
        '.x3f',
        '.erf',
        '.mef',
        '.rwl',
        '.iiq',
      ],
      currentHandlerName: 'Windows RAW Image Extension',
      statusText: 'External',
      isEnabled: false,
      isPartiallyEnabled: false,
      hasExternalHandler: true,
      recommended: true,
    },
  ];

  const enabledMask = families.reduce(
    (mask, f) => (f.isEnabled || f.isPartiallyEnabled ? mask | (1 << f.bitIndex) : mask),
    0
  );

  return {
    bundlePresent: true,
    bundleError: null,
    isElevated: false,
    adminSettingEnabled: false,
    enabledMask,
    hasMachineLevelCodecsOrBackup: false,
    families,
  };
}

export function buildOptimizerTweaks(
  profile: OptimizerProfileDto,
  appliedStates: Record<string, 'Default' | 'PartiallyOptimized' | 'Optimized'>
): OptimizerTweakDto[] {
  return [
    {
      id: 'preventThumbCacheCleanup',
      title: 'Disable Automatic Cache Cleanup (Autorun = 0)',
      tab: 'cacheNetwork',
      requiresAdmin: true,
      enabled: profile.preventThumbCacheCleanup,
      state: appliedStates.preventThumbCacheCleanup ?? 'Default',
      whatItDoes:
        "Sets 'Autorun = 0' (DWORD) across HKCU, HKLM, and WOW6432Node under SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VolumeCaches\\Thumbnail Cache.",
      pros: 'Most important safeguard against unexpected thumbnail cache resets. Stops Windows 10/11 Automatic Maintenance from silently wiping thumbcache_*.db files when idle.',
      cons: 'Your thumbcache_*.db files will continue to grow on disk as more folders are preloaded (managed automatically by the 1.6 GB bucket spillover and backup engine).',
    },
    {
      id: 'clearDiskCleanupFlags',
      title: 'Clear Disk Cleanup Thumbnail Flags (StateFlags)',
      tab: 'cacheNetwork',
      requiresAdmin: true,
      enabled: profile.clearDiskCleanupFlags,
      state: appliedStates.clearDiskCleanupFlags ?? 'Default',
      whatItDoes:
        "Zeroes out all 'StateFlags0000' through 'StateFlags9999' values under the VolumeCaches\\Thumbnail Cache registry key.",
      pros: 'Ensures saved or automated Disk Cleanup profiles (cleanmgr.exe /sagerun) never include the thumbnail cache for deletion.',
      cons: "If you manually open Windows Disk Cleanup later and intentionally want to delete thumbnails there, you will need to re-check 'Thumbnails'.",
    },
    {
      id: 'disableSilentCleanupTask',
      title: "Disable 'SilentCleanup' Scheduled Task (Admin)",
      tab: 'cacheNetwork',
      requiresAdmin: true,
      enabled: profile.disableSilentCleanupTask,
      state: appliedStates.disableSilentCleanupTask ?? 'Default',
      whatItDoes:
        "Disables the '\\Microsoft\\Windows\\DiskCleanup\\SilentCleanup' scheduled task in Windows Task Scheduler (requires Administrator).",
      pros: 'Completely blocks Windows from running background Disk Cleanup (cleanmgr.exe) during idle maintenance windows.',
      cons: "Also stops automatic cleanup of %TEMP% and old Windows Update logs. For most users, keeping 'Autorun = 0' enabled already protects thumbnails without disabling this task.",
    },
    {
      id: 'disableNetworkThumbsDb',
      title: 'Disable Thumbs.db on Network Folders (SMB/NAS)',
      tab: 'cacheNetwork',
      requiresAdmin: false,
      enabled: profile.disableNetworkThumbsDb,
      state: appliedStates.disableNetworkThumbsDb ?? 'Default',
      whatItDoes:
        "Sets 'DisableThumbsDBOnNetworkFolders = 1' (DWORD) in HKCU Explorer Policies and Explorer\\Advanced.",
      pros: "Eliminates 'file is open in Windows Explorer' lock errors when renaming or deleting SMB/NAS folders, and stores network thumbnails in your fast local SSD cache.",
      cons: 'Other computers on the network will not share a pre-generated Thumbs.db inside the remote folder (each PC maintains its own local cache).',
    },
    {
      id: 'forceThumbnailPreviews',
      title: 'Force Thumbnail Previews & Local Cache Active',
      tab: 'cacheNetwork',
      requiresAdmin: false,
      enabled: profile.forceThumbnailPreviews,
      state: appliedStates.forceThumbnailPreviews ?? 'Default',
      whatItDoes:
        "Sets 'IconsOnly = 0' in Explorer\\Advanced and 'DisableThumbnailCache = 0' / 'NoThumbnailCache = 0' in Explorer Policies.",
      pros: 'Ensures Windows Explorer always renders real image and video thumbnails (instead of static file icons) and keeps local thumbnail caching enabled.',
      cons: 'On very old machines with slow mechanical hard drives, rendering thumbnails uses slightly more CPU and disk I/O than static icons.',
    },
    {
      id: 'disableFolderTypeDiscovery',
      title: 'Disable Automatic Folder Type Discovery',
      tab: 'explorer',
      requiresAdmin: false,
      enabled: profile.disableFolderTypeDiscovery,
      state: appliedStates.disableFolderTypeDiscovery ?? 'Default',
      whatItDoes:
        "Sets 'FolderType = NotSpecified' under HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\\AllFolders\\Shell.",
      pros: 'Stops Explorer from sniffing every file to guess Pictures/Videos/Music/General Items. Makes folders with thousands of media files open instantaneously.',
      cons: "Newly opened folders default to the 'General Items' column template (you can still freely change icon sizes and view modes in any folder).",
    },
    {
      id: 'expandBagMruSize',
      title: 'Expand Explorer Folder View Memory (BagMRU Size)',
      tab: 'explorer',
      requiresAdmin: false,
      enabled: profile.expandBagMruSize,
      state: appliedStates.expandBagMruSize ?? 'Default',
      whatItDoes:
        "Increases 'BagMRU Size' from 5,000 (Windows default) to 20,000 folders under the Shell and ShellNoRoam registry keys.",
      pros: 'Prevents Explorer from forgetting your custom thumbnail sizes (Large / Extra Large Icons) and sort order after browsing more than 5,000 folders.',
      cons: 'Uses a few extra megabytes in your user registry hive (UsrClass.dat) as thousands of folder view preferences are stored.',
    },
    {
      id: 'expandIconCache',
      title: 'Expand Windows System Icon Cache',
      tab: 'explorer',
      requiresAdmin: true,
      enabled: profile.expandIconCache,
      state: appliedStates.expandIconCache ?? 'Default',
      whatItDoes:
        "Sets 'Max Cached Icons' under HKLM and HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer, expanding the icon cache from ~500 KB (4 MB, 8 MB, or 16 MB).",
      pros: 'Prevents desktop, taskbar, and Explorer icons from turning blank white or flickering and redrawing slowly.',
      cons: 'Uses 4 MB to 16 MB of disk/RAM for the icon cache and requires restarting Windows Explorer (or signing out) to take full effect.',
    },
    {
      id: 'enforceOnStartup',
      title: 'Re-apply on App Startup',
      tab: 'shell',
      requiresAdmin: false,
      enabled: profile.enforceOnStartup,
      state: profile.enforceOnStartup ? 'Optimized' : 'Default',
      whatItDoes:
        'Automatically re-applies your saved Windows optimizations every time WinThumbsPreloader starts.',
      pros: 'Prevents major Windows Feature Updates from silently resetting thumbnail cache protection back to defaults (Autorun = 3) with zero manual effort.',
      cons: 'If the application starts without Administrator privileges, only user-scoped (HKCU) settings are re-applied at startup.',
    },
  ];
}

export const OPTIMIZER_SHELL_TOOLS: OptimizerShellToolHelpDto[] = [
  {
    id: 'contextMenu',
    title: 'Explorer Context Menu',
    whatItDoes:
      "Registers or removes the cascading 'WinThumbsPreloader' right-click context menu for Folders, Folder Backgrounds, and Drives.",
    pros: 'Works even in Portable mode without Administrator privileges (via HKCU\\Software\\Classes). Lets you preload any folder with one right-click.',
    cons: "If you move the portable executable to a different folder later, click 'Register Context Menu' again to update the path.",
  },
  {
    id: 'rebuildIconCache',
    title: 'Rebuild Icon Cache Only',
    whatItDoes:
      "Deletes only 'iconcache_*.db' and 'IconCache.db' files and runs 'ie4uinit.exe -show' to refresh Windows system icons.",
    pros: "Fixes corrupted or blank application/shortcut icons WITHOUT touching any 'thumbcache_*.db' files (your preloaded thumbnails remain 100% intact).",
    cons: 'Any iconcache_*.db files currently locked by explorer.exe will only be fully recreated after restarting Windows Explorer.',
  },
  {
    id: 'restartExplorer',
    title: 'Restart Windows Explorer',
    whatItDoes:
      "Cleanly terminates 'explorer.exe' (exit code 1) and immediately relaunches it.",
    pros: 'Applies ShellBag, Folder Type Discovery, and Icon Cache changes immediately without requiring a system reboot or sign-out.',
    cons: 'Closes currently open File Explorer windows and interrupts any active file copy/move operations being performed by Explorer.',
  },
  {
    id: 'restoreDefaults',
    title: 'Restore Windows Defaults',
    whatItDoes:
      'Removes all optimization keys and policies managed by this module and restores standard Windows 10/11 defaults (Autorun = 3, default BagMRU, SilentCleanup enabled, etc.).',
    pros: 'Guarantees 100% reversibility if you ever want to return Windows to its stock out-of-the-box behavior.',
    cons: 'Once Windows defaults are restored, Windows Automatic Maintenance will resume periodically purging the thumbnail cache during idle periods.',
  },
];

export function buildInitialOptimizerState(): OptimizerStateDto {
  const profile: OptimizerProfileDto = {
    enforceOnStartup: true,
    preventThumbCacheCleanup: true,
    clearDiskCleanupFlags: false,
    disableSilentCleanupTask: false,
    disableNetworkThumbsDb: false,
    forceThumbnailPreviews: false,
    disableFolderTypeDiscovery: false,
    expandBagMruSize: false,
    expandIconCache: false,
    iconCacheSizeKb: 8192,
  };
  const appliedStates: Record<string, 'Default' | 'PartiallyOptimized' | 'Optimized'> = {
    preventThumbCacheCleanup: 'PartiallyOptimized',
  };
  return {
    isElevated: false,
    needsElevationForProfile: true,
    contextMenuRegistered: true,
    contextMenuSystemWide: false,
    currentIconCacheSizeKb: 500,
    configuredIconCacheSizeKb: 8192,
    enforceOnStartup: true,
    profile,
    tweaks: buildOptimizerTweaks(profile, appliedStates),
    shellTools: OPTIMIZER_SHELL_TOOLS,
  };
}

export function buildInitialSettingsState(): SettingsStateDto {
  return {
    general: {
      admin: false,
      isRunningAsAdmin: false,
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
    },
    extensions: {
      preloadFolderIcons: false,
      preloadAllFolders: false,
      extensionsAutoFormatting: 'Disabled',
      extensionsText: DEFAULT_EXTENSIONS_TEXT,
      defaultExtensionsText: DEFAULT_EXTENSIONS_TEXT,
    },
    logging: {
      loggingFrequency: 0,
      loggerFolderPath: '',
      recommendedDefaultLogPath:
        'C:\\Users\\Demo\\AppData\\Local\\WinThumbsPreloader\\Logs',
      logFolderStatus: {
        status: 'NotSelected',
        message: 'Status: No log folder selected.',
        normalizedPath: '',
        isRunningAsAdmin: false,
        usesDefaultPath: false,
        canUseForLogging: false,
      },
      logsSizeBytes: 0,
      logsSizeFormatted: 'N/A',
      autoDeleteLogsByAge: false,
      logRetentionDays: 30,
      maxLogFileSizeMB: 1024,
    },
    about: {
      appName: 'WinThumbsPreloader',
      version: '3.4.0',
      isRunningAsAdmin: false,
      projectUrl: 'https://github.com/joaoschlickmann/WinThumbsPreloader-V3',
      licenseText:
        'MIT License\n\nCopyright (c) 2018 Dmitry Bruhov, 2026 Mutahar Farooq, 2026 João Schlickmann\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.',
    },
  };
}

export function organizeExtensionsMock(text: string, method: string): string {
  const parts = text
    .split(/[,\s\r\n]+/)
    .map((s) => s.trim())
    .filter(Boolean);
  switch (method) {
    case 'Vertically':
      return parts.join('\n');
    case 'Commas':
      return parts.join(',');
    case 'Spaces':
      return parts.join(' ');
    case 'CommasAndSpaces':
    case 'Commas and Spaces':
      return parts.join(', ');
    case 'Disabled':
    default:
      return text;
  }
}
