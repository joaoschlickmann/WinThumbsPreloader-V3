using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Xunit;

namespace WinThumbsPreloader.Tests
{
    public sealed class CodecManagerTests : IDisposable
    {
        private readonly string _testRootKeyPath;
        private readonly string _tempDir;
        private readonly string _fakeDllPath;
        private readonly string _backupFilePath;

        public CodecManagerTests()
        {
            _testRootKeyPath = @"Software\WinThumbsPreloader_CodecTests\" + Guid.NewGuid().ToString("N");
            _tempDir = Path.Combine(Path.GetTempPath(), "WTP_CodecTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _fakeDllPath = Path.Combine(_tempDir, "WinThumbsCodecs.dll");
            File.WriteAllBytes(_fakeDllPath, [0x4D, 0x5A, 0x90, 0x00]); // minimal stub for unit tests
            _backupFilePath = Path.Combine(_tempDir, "codecs-backup.json");
        }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_testRootKeyPath, throwOnMissingSubKey: false);
            }
            catch { }

            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void Options_ApplyAndUninstallCodecsFlags_ParseAndValidateStrictly()
        {
            var applyAll = new Options(["-apply-codecs", "-codecs-mask:0x3FF"]);
            Assert.True(applyAll.applyCodecs);
            Assert.Equal(CodecManager.AllCodecsMask, applyAll.codecsMask);
            Assert.False(applyAll.unknownFlagProvided);
            Assert.False(applyAll.IsGuiLaunch);

            var applyDec = new Options(["-apply-codecs", "-codecs-mask:15"]);
            Assert.True(applyDec.applyCodecs);
            Assert.Equal(15, applyDec.codecsMask);
            Assert.False(applyDec.IsGuiLaunch);

            var missingMask = new Options(["-apply-codecs"]);
            Assert.False(missingMask.applyCodecs);
            Assert.True(missingMask.unknownFlagProvided);

            var outOfRange = new Options(["-apply-codecs", "-codecs-mask:0x400"]);
            Assert.False(outOfRange.applyCodecs);
            Assert.True(outOfRange.unknownFlagProvided);

            var negativeMask = new Options(["-apply-codecs", "-codecs-mask:-1"]);
            Assert.False(negativeMask.applyCodecs);
            Assert.True(negativeMask.unknownFlagProvided);

            var extraSubArg = new Options(["-apply-codecs", "-codecs-mask:0x001", "-unexpected"]);
            Assert.False(extraSubArg.applyCodecs);
            Assert.True(extraSubArg.unknownFlagProvided);

            var uninstall = new Options(["-uninstall-codecs"]);
            Assert.True(uninstall.uninstallCodecs);
            Assert.False(uninstall.unknownFlagProvided);
            Assert.False(uninstall.IsGuiLaunch);

            var badUninstall = new Options(["-uninstall-codecs", "-extra"]);
            Assert.False(badUninstall.uninstallCodecs);
            Assert.True(badUninstall.unknownFlagProvided);

            var reopen = new Options(["-reopencodecs"]);
            Assert.True(reopen.reopenCodecs);
            Assert.True(reopen.IsGuiLaunch);
        }

        [Fact]
        public void RunElevatedHelper_RejectsShellMetacharactersForCodecFlags()
        {
            Assert.False(WindowsOptimizer.RunElevatedHelper("-apply-codecs -codecs-mask:0x3FF & calc"));
            Assert.False(WindowsOptimizer.RunElevatedHelper("-apply-codecs -codecs-mask:0x3FF;notepad"));
            Assert.False(WindowsOptimizer.RunElevatedHelper("-uninstall-codecs | whoami"));
        }

        [Fact]
        public void CodecManager_InstallDetectIdempotentReinstallAndRestorePreviousHandlers_InIsolatedScope()
        {
            var scope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                Registry.CurrentUser,
                _testRootKeyPath,
                simulateMachineWritable: true);

            const string ardfryClsid = "{11111111-1111-1111-1111-111111111111}";
            const string acrobatClsid = "{22222222-2222-2222-2222-222222222222}";
            const string powerToysClsid = "{33333333-3333-3333-3333-333333333333}";

            // Seed simulated Ardfry PSD Codec, Adobe Acrobat (UserChoice), and PowerToys SVG handler.
            using (var k = scope.OpenMachineKey($@"Software\Classes\CLSID\{ardfryClsid}", writable: true))
                k?.SetValue(string.Empty, "Ardfry PSD Codec Thumbnail Handler");
            using (var k = scope.OpenMachineKey($@"Software\Classes\CLSID\{ardfryClsid}\InprocServer32", writable: true))
                k?.SetValue(string.Empty, @"C:\Program Files\Ardfry Imaging\PSD Codec\psdcodec.dll");
            using (var k = scope.OpenMachineKey(@"Software\Classes\.psd", writable: true))
                k?.SetValue("Treatment", 2, RegistryValueKind.DWord);
            using (var k = scope.OpenMachineKey($@"Software\Classes\.psd\ShellEx\{CodecManager.ThumbnailProviderIid}", writable: true))
                k?.SetValue(string.Empty, ardfryClsid);

            using (var k = scope.OpenMachineKey($@"Software\Classes\CLSID\{acrobatClsid}", writable: true))
                k?.SetValue(string.Empty, "Adobe Acrobat PDF Preview Handler");
            using (var k = scope.OpenUserKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice", writable: true))
                k?.SetValue("ProgId", "AcroExch.Document.DC");
            using (var k = scope.OpenMachineKey($@"Software\Classes\AcroExch.Document.DC\ShellEx\{CodecManager.ThumbnailProviderIid}", writable: true))
                k?.SetValue(string.Empty, acrobatClsid);

            using (var k = scope.OpenMachineKey($@"Software\Classes\CLSID\{powerToysClsid}", writable: true))
                k?.SetValue(string.Empty, "Microsoft PowerToys SvgThumbnailProvider");
            using (var k = scope.OpenMachineKey($@"Software\Classes\.svg\ShellEx\{CodecManager.ThumbnailProviderIid}", writable: true))
                k?.SetValue(string.Empty, powerToysClsid);

            // 1. Detect initial external handlers
            var initial = CodecManager.DetectAllStatuses(scope);
            Assert.Equal("Ardfry PSD Codec", initial.Single(s => s.Family.Id == CodecFamilyId.PsdPsb).SummaryHandlerName);
            Assert.True(initial.Single(s => s.Family.Id == CodecFamilyId.PsdPsb).HasExternalHandler);
            Assert.Equal("Adobe Acrobat", initial.Single(s => s.Family.Id == CodecFamilyId.Pdf).SummaryHandlerName);
            Assert.Equal("Microsoft PowerToys", initial.Single(s => s.Family.Id == CodecFamilyId.Svg).SummaryHandlerName);
            Assert.Equal("None", initial.Single(s => s.Family.Id == CodecFamilyId.Affinity).SummaryHandlerName);

            // 2. Install all 10 families
            var installResult = CodecManager.ApplyCodecSelection(
                CodecManager.AllCodecsMask,
                scope,
                _backupFilePath,
                _fakeDllPath,
                broadcastShellChange: false);

            Assert.Equal(CodecManager.AllCodecsMask, installResult.AppliedMask);
            Assert.Equal(10, installResult.EnabledFamiliesCount);
            Assert.True(installResult.UserScopeApplied);
            Assert.True(installResult.MachineScopeApplied);
            Assert.True(File.Exists(_backupFilePath));

            var afterInstall = CodecManager.DetectAllStatuses(scope);
            Assert.All(afterInstall, s =>
            {
                Assert.True(s.IsEnabled, $"Expected {s.Family.Id} to be enabled.");
                Assert.Equal("WinThumbsPreloader Codecs", s.SummaryHandlerName);
            });

            // Verify UserChoice was NOT modified and DisableProcessIsolation was NEVER set.
            using (var uc = scope.OpenUserKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice", writable: false))
                Assert.Equal("AcroExch.Document.DC", uc?.GetValue("ProgId")?.ToString());
            foreach (var fam in CodecManager.Families)
            {
                using var clsidKey = scope.OpenMachineKey($@"Software\Classes\CLSID\{fam.Clsid}", writable: false);
                Assert.Null(clsidKey?.GetValue("DisableProcessIsolation"));
            }

            // 3. Re-apply idempotently (must NOT overwrite original backup entries!)
            CodecManager.ApplyCodecSelection(
                CodecManager.AllCodecsMask,
                scope,
                _backupFilePath,
                _fakeDllPath,
                broadcastShellChange: false);

            // 4. Disable only PDF -> restores Adobe Acrobat while others stay enabled
            int withoutPdfMask = CodecManager.AllCodecsMask & ~(int)CodecFamilyId.Pdf;
            CodecManager.ApplyCodecSelection(
                withoutPdfMask,
                scope,
                _backupFilePath,
                _fakeDllPath,
                broadcastShellChange: false);

            var afterPdfDisable = CodecManager.DetectAllStatuses(scope);
            Assert.False(afterPdfDisable.Single(s => s.Family.Id == CodecFamilyId.Pdf).IsEnabled);
            Assert.Equal("Adobe Acrobat", afterPdfDisable.Single(s => s.Family.Id == CodecFamilyId.Pdf).SummaryHandlerName);
            Assert.True(afterPdfDisable.Single(s => s.Family.Id == CodecFamilyId.PsdPsb).IsEnabled);

            // 5. Full uninstall -> restores Ardfry (including Treatment=2), PowerToys, and removes backup + our CLSIDs
            var uninstallResult = CodecManager.UninstallAllCodecs(scope, _backupFilePath, broadcastShellChange: false);
            Assert.True(uninstallResult.BackupRestored);
            Assert.False(File.Exists(_backupFilePath));

            var afterUninstall = CodecManager.DetectAllStatuses(scope);
            Assert.Equal("Ardfry PSD Codec", afterUninstall.Single(s => s.Family.Id == CodecFamilyId.PsdPsb).SummaryHandlerName);
            Assert.Equal("Adobe Acrobat", afterUninstall.Single(s => s.Family.Id == CodecFamilyId.Pdf).SummaryHandlerName);
            Assert.Equal("Microsoft PowerToys", afterUninstall.Single(s => s.Family.Id == CodecFamilyId.Svg).SummaryHandlerName);
            Assert.Equal("None", afterUninstall.Single(s => s.Family.Id == CodecFamilyId.Affinity).SummaryHandlerName);

            using (var psdMeta = scope.OpenMachineKey(@"Software\Classes\.psd", writable: false))
                Assert.Equal(2, psdMeta?.GetValue("Treatment"));
            foreach (var fam in CodecManager.Families)
            {
                using var clsidUser = scope.OpenUserKey($@"Software\Classes\CLSID\{fam.Clsid}", writable: false);
                using var clsidMach = scope.OpenMachineKey($@"Software\Classes\CLSID\{fam.Clsid}", writable: false);
                Assert.Null(clsidUser);
                Assert.Null(clsidMach);
            }
        }

        [Fact]
        public void CodecManager_PathSecurity_RejectsJunctionInDllOrBackupPath()
        {
            var scope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                Registry.CurrentUser,
                _testRootKeyPath,
                simulateMachineWritable: false);

            string realSubDir = Path.Combine(_tempDir, "real_codecs");
            Directory.CreateDirectory(realSubDir);
            string realDll = Path.Combine(realSubDir, "WinThumbsCodecs.dll");
            File.WriteAllBytes(realDll, [0x4D, 0x5A]);

            string junctionDir = Path.Combine(_tempDir, "junction_codecs");
            using (var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionDir}\" \"{realSubDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                proc?.WaitForExit(5000);
            }

            try
            {
                string junctionDll = Path.Combine(junctionDir, "WinThumbsCodecs.dll");
                Assert.Throws<InvalidOperationException>(() =>
                    CodecManager.ApplyCodecSelection(
                        (int)CodecFamilyId.PsdPsb,
                        scope,
                        _backupFilePath,
                        junctionDll,
                        broadcastShellChange: false));
            }
            finally
            {
                try
                {
                    if (Directory.Exists(junctionDir))
                        Directory.Delete(junctionDir, recursive: false);
                }
                catch { }
            }
        }

        [Fact]
        public void CodecManager_MergeEnabledCodecExtensionsIntoSettings_MergesWithoutDuplicates()
        {
            string merged = CodecManager.MergeEnabledCodecExtensionsIntoSettings(
                (int)(CodecFamilyId.PsdPsb | CodecFamilyId.Pdf | CodecFamilyId.InDesign | CodecFamilyId.AdobeXd),
                "jpg, png, psd, SVG");

            var tokens = merged.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Contains("psd", tokens);
            Assert.Contains("psb", tokens);
            Assert.Contains("pdf", tokens);
            Assert.Contains("indd", tokens);
            Assert.Contains("idml", tokens);
            Assert.Contains("xd", tokens);
            Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void CodecManager_HasMachineLevelCodecsOrBackup_DetectsMachineKeyAndBackupManifest()
        {
            var scope = WindowsOptimizer.RegistryHiveScope.CreateIsolatedForTesting(
                Registry.CurrentUser,
                _testRootKeyPath,
                simulateMachineWritable: true);

            Assert.False(CodecManager.HasMachineLevelCodecsOrBackup(scope, _backupFilePath));

            // 1. Machine HKLM CLSID present -> true, then uninstall -> false
            CodecManager.ApplyCodecSelection(
                (int)CodecFamilyId.PsdPsb,
                scope,
                _backupFilePath,
                _fakeDllPath,
                broadcastShellChange: false);

            Assert.True(CodecManager.HasMachineLevelCodecsOrBackup(scope, _backupFilePath));

            CodecManager.UninstallAllCodecs(scope, _backupFilePath, broadcastShellChange: false);
            Assert.False(CodecManager.HasMachineLevelCodecsOrBackup(scope, _backupFilePath));

            // 2. Non-HKLM (HKCU only) backup entry -> false
            File.WriteAllText(_backupFilePath, """
                {
                  "Version": 1,
                  "Entries": {
                    "HKCU|Software\\Classes\\.psd\\ShellEx\\{e357fccd-a995-4576-b01f-234630154e96}": {
                      "Hive": "HKCU",
                      "SubKey": "Software\\Classes\\.psd\\ShellEx\\{e357fccd-a995-4576-b01f-234630154e96}",
                      "Kind": "ShellEx",
                      "KeyExistedBefore": false
                    }
                  }
                }
                """, Encoding.UTF8);
            Assert.False(CodecManager.HasMachineLevelCodecsOrBackup(scope, _backupFilePath));

            // 3. HKLM entry in backup manifest -> true
            File.WriteAllText(_backupFilePath, """
                {
                  "Version": 1,
                  "Entries": {
                    "HKLM|Software\\Classes\\.psd\\ShellEx\\{e357fccd-a995-4576-b01f-234630154e96}": {
                      "Hive": "HKLM",
                      "SubKey": "Software\\Classes\\.psd\\ShellEx\\{e357fccd-a995-4576-b01f-234630154e96}",
                      "Kind": "ShellEx",
                      "KeyExistedBefore": true,
                      "PreviousDefaultValue": "{11111111-1111-1111-1111-111111111111}"
                    }
                  }
                }
                """, Encoding.UTF8);
            Assert.True(CodecManager.HasMachineLevelCodecsOrBackup(scope, _backupFilePath));
        }

        [Fact]
        public void NativeCodecsDll_LoadLibraryEx_ExposesComExportsWithoutRegistrySideEffects()
        {
            string repoRoot = FindRepoRoot();
            string[] candidates =
            [
                Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader", "bin", "Release", "net10.0-windows7.0", "win-x64", "Codecs", "WinThumbsCodecs.dll"),
                Path.Combine(repoRoot, "WinThumbsCodecs", "target", "release", "WinThumbsCodecs.dll"),
                Path.Combine(repoRoot, "WinThumbsPreloader", "WinThumbsPreloader", "bin", "Debug", "net10.0-windows7.0", "win-x64", "Codecs", "WinThumbsCodecs.dll"),
                Path.Combine(repoRoot, "WinThumbsCodecs", "target", "debug", "WinThumbsCodecs.dll")
            ];

            string? dllPath = candidates.FirstOrDefault(File.Exists);
            Assert.True(dllPath != null, $"Compiled WinThumbsCodecs.dll not found in any expected path:\n{string.Join("\n", candidates)}");

            const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
            const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

            IntPtr module = LoadLibraryExW(
                dllPath!,
                IntPtr.Zero,
                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            try
            {
                Assert.True(module != IntPtr.Zero, $"LoadLibraryExW failed with Win32 error {Marshal.GetLastWin32Error()} for '{dllPath}'.");
                Assert.NotEqual(IntPtr.Zero, GetProcAddress(module, "DllGetClassObject"));
                Assert.NotEqual(IntPtr.Zero, GetProcAddress(module, "DllCanUnloadNow"));
            }
            finally
            {
                if (module != IntPtr.Zero)
                {
                    FreeLibrary(module);
                }
            }
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InnoSetup", "installer.iss")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);
    }
}
