using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static WinThumbsPreloader.Logger;

#nullable enable

namespace WinThumbsPreloader
{
    /// <summary>
    /// Provides resolution and detection for UNC network paths (\\server\share) and mapped network drives (e.g. Z:\),
    /// including seamless fallback through HKCU\Network when running elevated (UAC split-token where mapped drives
    /// are invisible to the administrator logon session).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class NetworkPathResolver
    {
        private const int UNIVERSAL_NAME_INFO_LEVEL = 0x00000001;
        private const int ERROR_MORE_DATA = 234;
        private const int NO_ERROR = 0;

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetUniversalNameW(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpLocalPath,
            [In] int dwInfoLevel,
            [In] IntPtr lpBuffer,
            [In, Out] ref int lpBufferSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct UNIVERSAL_NAME_INFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpUniversalName;
        }

        /// <summary>
        /// Normalizes and resolves a local path, mapped network drive path, or UNC network path.
        /// If a mapped drive letter (e.g., Z:\Photos) is inaccessible in an elevated process,
        /// automatically resolves it to its underlying UNC path (\\server\share\Photos).
        /// </summary>
        public static string? NormalizeAndResolvePath(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            string p = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"')).Replace('/', '\\');

            // Case: "C:" or "Z:" -> treat as "C:\" or "Z:\"
            if (p.Length == 2 && char.IsLetter(p[0]) && p[1] == ':')
            {
                p += "\\";
            }

            // Trim trailing slash on UNC paths except when it's just \\server\share\
            if (IsUncPath(p))
            {
                return NormalizeUncPath(p);
            }

            if (!Path.IsPathRooted(p))
                return null;

            try
            {
                p = Path.GetFullPath(p);
            }
            catch
            {
                return null;
            }

            if (Directory.Exists(p) || File.Exists(p))
                return p;

            // If it doesn't exist directly, check if it's a mapped network drive hidden by UAC elevation
            if (TryResolveMappedDriveToUnc(p, out string? uncPath) && !string.IsNullOrWhiteSpace(uncPath))
            {
                if (Directory.Exists(uncPath) || File.Exists(uncPath))
                {
                    WriteLine($"Resolved mapped drive path '{p}' to UNC path '{uncPath}'", LoggingFrequency.PreloaderLogging);
                    return uncPath;
                }
            }

            return null;
        }

        public static bool IsUncPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.StartsWith(@"\\", StringComparison.Ordinal);
        }

        public static bool IsNetworkOrMappedPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (IsUncPath(path))
                return true;

            try
            {
                string? root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && char.IsLetter(root[0]) && root[1] == ':')
                {
                    try
                    {
                        var driveInfo = new DriveInfo(root);
                        if (driveInfo.DriveType == DriveType.Network)
                            return true;
                    }
                    catch
                    {
                        // Ignore DriveInfo errors and check registry fallback
                    }

                    if (TryGetMappedDriveUncRoot(root[0], out _))
                        return true;
                }
            }
            catch
            {
                // Ignore
            }

            return false;
        }

        private static string? NormalizeUncPath(string uncPath)
        {
            try
            {
                string normalized = Path.GetFullPath(uncPath);
                if (Directory.Exists(normalized) || File.Exists(normalized))
                    return normalized;
            }
            catch
            {
                // Path.GetFullPath can fail on bare \\server\share in some edge cases
            }

            string trimmed = uncPath.TrimEnd('\\');
            if (Directory.Exists(trimmed) || File.Exists(trimmed))
                return trimmed;

            if (Directory.Exists(uncPath) || File.Exists(uncPath))
                return uncPath;

            return null;
        }

        /// <summary>
        /// Attempts to convert a local/mapped drive path (e.g., "Z:\Folder\File.jpg") to its UNC path
        /// ("\\server\share\Folder\File.jpg") using WNetGetUniversalNameW or HKCU\Network fallback.
        /// </summary>
        public static bool TryResolveMappedDriveToUnc(string path, out string? uncPath)
        {
            uncPath = null;

            if (string.IsNullOrWhiteSpace(path) || path.Length < 2 || !char.IsLetter(path[0]) || path[1] != ':')
                return false;

            // 1. Try WNetGetUniversalNameW first
            if (TryWNetGetUniversalName(path, out string? wnetUnc) && !string.IsNullOrWhiteSpace(wnetUnc))
            {
                uncPath = wnetUnc;
                return true;
            }

            // 2. Fallback to HKCU\Network\<DriveLetter>\RemotePath (works even when elevated under UAC split-token)
            char driveLetter = char.ToUpperInvariant(path[0]);
            if (TryGetMappedDriveUncRoot(driveLetter, out string? remoteRoot) && !string.IsNullOrWhiteSpace(remoteRoot))
            {
                string relativePart = path.Length > 2 ? path.Substring(2).TrimStart('\\') : string.Empty;
                uncPath = string.IsNullOrEmpty(relativePart)
                    ? remoteRoot.TrimEnd('\\')
                    : Path.Combine(remoteRoot.TrimEnd('\\'), relativePart);
                return true;
            }

            return false;
        }

        private static bool TryWNetGetUniversalName(string localPath, out string? universalName)
        {
            universalName = null;
            int bufferSize = 1024;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                buffer = Marshal.AllocHGlobal(bufferSize);
                int result = WNetGetUniversalNameW(localPath, UNIVERSAL_NAME_INFO_LEVEL, buffer, ref bufferSize);

                if (result == ERROR_MORE_DATA)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    result = WNetGetUniversalNameW(localPath, UNIVERSAL_NAME_INFO_LEVEL, buffer, ref bufferSize);
                }

                if (result == NO_ERROR)
                {
                    var info = Marshal.PtrToStructure<UNIVERSAL_NAME_INFO>(buffer);
                    if (!string.IsNullOrWhiteSpace(info.lpUniversalName))
                    {
                        universalName = info.lpUniversalName;
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore P/Invoke exceptions
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }

            return false;
        }

        public static bool TryGetMappedDriveUncRoot(char driveLetter, out string? remotePath)
        {
            remotePath = null;
            string subKeyPath = $@"Network\{char.ToUpperInvariant(driveLetter)}";

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
                if (key?.GetValue("RemotePath") is string remote && !string.IsNullOrWhiteSpace(remote))
                {
                    remotePath = remote.Trim();
                    return true;
                }
            }
            catch
            {
                // Ignore registry errors
            }

            return false;
        }

        /// <summary>
        /// Returns all mapped network drives registered for the current user (e.g., 'Z' -> '\\server\share').
        /// </summary>
        public static Dictionary<char, string> GetUserMappedDrives()
        {
            var result = new Dictionary<char, string>();

            try
            {
                using RegistryKey? networkKey = Registry.CurrentUser.OpenSubKey("Network", writable: false);
                if (networkKey != null)
                {
                    foreach (string subKeyName in networkKey.GetSubKeyNames())
                    {
                        if (subKeyName.Length == 1 && char.IsLetter(subKeyName[0]))
                        {
                            char letter = char.ToUpperInvariant(subKeyName[0]);
                            using RegistryKey? driveKey = networkKey.OpenSubKey(subKeyName, writable: false);
                            if (driveKey?.GetValue("RemotePath") is string remotePath && !string.IsNullOrWhiteSpace(remotePath))
                            {
                                result[letter] = remotePath.Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry errors
            }

            return result;
        }
    }
}
