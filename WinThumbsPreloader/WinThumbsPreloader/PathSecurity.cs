using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

#nullable enable

namespace WinThumbsPreloader
{
    /// <summary>
    /// Centralized filesystem security helpers for detecting NTFS reparse points (junctions, symlinks,
    /// mount points), resolving canonical directory targets to prevent traversal loops, and enforcing
    /// bounded regular-file checks before reading, writing, or deleting state/cache files.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class PathSecurity
    {
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFFu;
        private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x0400u;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000u;
        private const uint VOLUME_NAME_DOS = 0x0u;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint GetFileAttributesW(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            uint cchFilePath,
            uint dwFlags);

        /// <summary>
        /// Fast single-syscall check for <see cref="FileAttributes.ReparsePoint"/> on an existing path.
        /// Returns <c>false</c> if the path is null, empty, or does not exist.
        /// </summary>
        public static bool IsReparsePoint(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            uint attrs = GetFileAttributesW(path);
            if (attrs == INVALID_FILE_ATTRIBUTES)
                return false;

            return (attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        }

        /// <summary>
        /// Determines whether an existing path is a redirecting link, NTFS junction, or volume mount point
        /// (as opposed to an in-place cloud sync placeholder such as OneDrive Files On-Demand).
        /// </summary>
        public static bool IsLinkOrJunction(string? path)
        {
            if (!IsReparsePoint(path))
                return false;

            try
            {
                string fullPath = Path.GetFullPath(path!);
                FileSystemInfo info = Directory.Exists(fullPath)
                    ? new DirectoryInfo(fullPath)
                    : new FileInfo(fullPath);

                if (info.LinkTarget != null)
                    return true;

                // Volume mount points (\??\Volume{GUID}\) may report a null LinkTarget in .NET;
                // verify that the OS-resolved final path matches the normalized path itself.
                if (!TryGetCanonicalPathKey(fullPath, out string? canonicalKey))
                    return true;

                string expectedKey = NormalizePathKey(fullPath);
                return !string.Equals(expectedKey, canonicalKey, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Checks whether <paramref name="fileOrDirectoryPath"/> or its parent hierarchy contains unsafe
        /// reparse points. When <paramref name="rejectAnyLeafOrParentReparsePoint"/> is <c>true</c>, any
        /// reparse point on the target itself or its immediate parent directory is rejected outright, and
        /// all higher ancestor directories are checked for redirecting junctions/symlinks.
        /// </summary>
        public static bool HasReparsePointOrJunctionInPath(
            string? fileOrDirectoryPath,
            bool rejectAnyLeafOrParentReparsePoint = true)
        {
            if (string.IsNullOrWhiteSpace(fileOrDirectoryPath))
                return true;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(fileOrDirectoryPath);
            }
            catch
            {
                return true;
            }

            if (rejectAnyLeafOrParentReparsePoint)
            {
                if (IsReparsePoint(fullPath))
                    return true;

                string? immediateParent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(immediateParent) && IsReparsePoint(immediateParent))
                    return true;
            }
            else if (IsLinkOrJunction(fullPath))
            {
                return true;
            }

            string? root = Path.GetPathRoot(fullPath);
            string? current = Path.GetDirectoryName(fullPath);

            while (!string.IsNullOrEmpty(current) &&
                   !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                if (IsLinkOrJunction(current))
                    return true;

                string? next = Path.GetDirectoryName(current);
                if (string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
                    break;

                current = next;
            }

            return false;
        }

        public static void EnsureSafeFileForWrite(string filePath)
        {
            if (HasReparsePointOrJunctionInPath(filePath, rejectAnyLeafOrParentReparsePoint: true))
            {
                throw new IOException($"Refusing to write through a reparse point or symbolic link: '{filePath}'.");
            }
        }

        public static void EnsureSafeFileForRead(string filePath)
        {
            if (HasReparsePointOrJunctionInPath(filePath, rejectAnyLeafOrParentReparsePoint: true))
            {
                throw new IOException($"Refusing to read through a reparse point or symbolic link: '{filePath}'.");
            }
        }

        public static bool IsSafeRegularFileWithinLimit(string? filePath, long maxBytes, out long length)
        {
            length = 0;

            if (maxBytes < 0 || HasReparsePointOrJunctionInPath(filePath, rejectAnyLeafOrParentReparsePoint: true))
                return false;

            try
            {
                var fileInfo = new FileInfo(filePath!);
                if (!fileInfo.Exists)
                    return false;

                FileAttributes attrs = fileInfo.Attributes;
                if ((attrs & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
                    return false;

                length = fileInfo.Length;
                return length >= 0 && length <= maxBytes;
            }
            catch
            {
                length = 0;
                return false;
            }
        }

        /// <summary>
        /// Resolves a directory (following any symlink, NTFS junction, or volume mount point) to a normalized
        /// canonical key suitable for cycle detection in <see cref="DirectoryScanner"/>.
        /// Returns <c>false</c> if the target is dangling, inaccessible, or self-referential at the OS level.
        /// </summary>
        public static bool TryGetCanonicalPathKey(string path, out string? canonicalKey)
        {
            canonicalKey = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                using SafeFileHandle handle = CreateFileW(
                    path,
                    0u,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    var sb = new StringBuilder(512);
                    uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
                    if (len >= sb.Capacity)
                    {
                        sb.EnsureCapacity((int)len + 1);
                        len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
                    }

                    if (len > 0 && len < sb.Capacity)
                    {
                        canonicalKey = NormalizePathKey(sb.ToString(0, (int)len));
                        return canonicalKey.Length > 0;
                    }
                }

                // Fallback for network/provider paths that do not support GetFinalPathNameByHandleW
                string fullPath = Path.GetFullPath(path);
                var dirInfo = new DirectoryInfo(fullPath);
                if (dirInfo.LinkTarget != null)
                {
                    FileSystemInfo? resolved = Directory.ResolveLinkTarget(fullPath, returnFinalTarget: true);
                    if (resolved == null || !resolved.Exists)
                        return false;

                    canonicalKey = NormalizePathKey(resolved.FullName);
                    return canonicalKey.Length > 0;
                }

                if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
                    return false;

                canonicalKey = NormalizePathKey(fullPath);
                return canonicalKey.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string NormalizePathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            ReadOnlySpan<char> span = path.AsSpan().Trim();
            bool strippedExtendedUnc = false;

            if (span.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                span = span.Slice(8);
                strippedExtendedUnc = true;
            }
            else if (span.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                span = span.Slice(4);
            }

            string normalized = strippedExtendedUnc
                ? @"\\" + span.ToString()
                : span.ToString();

            string? root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            }

            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
