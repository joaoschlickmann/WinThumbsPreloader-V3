using System;
using System.Collections.Generic;
using System.IO;
using static WinThumbsPreloader.Logger;

namespace WinThumbsPreloader
{
    sealed class Options
    {
        public bool badOrNoArguments;
        public bool includeNestedDirectories;
        public bool silentMode;
        public bool multiThreaded;
        public bool startMinimized;
        public bool reopenSettings;
        public bool reopenOptimizer;
        public bool applyOptimizations;
        public bool hasExplicitOptimizationProfile;
        public int optimizationProfileMask;
        public int optimizationIconCacheKb = 8192;
        public bool restoreDefaults;
        public bool shutdownExisting;
        public bool unknownFlagProvided;
        public bool invalidPathProvided;
        public string invalidPath = string.Empty;
        public int threadCount;
        public int maxParallelInstances = 0;
        public List<string> paths = new List<string>();

        public bool IsGuiLaunch =>
            !applyOptimizations &&
            !restoreDefaults &&
            !shutdownExisting &&
            !unknownFlagProvided &&
            (startMinimized || badOrNoArguments || paths == null || paths.Count == 0);

        public Options(string[] arguments)
        {
            WriteLine("Starting to parse arguments. - Options(string[] arguments)", LoggingFrequency.AllLogging);

            // Default options
            badOrNoArguments = (arguments.Length == 0);
            if (badOrNoArguments)
            {
                WriteLine("No arguments provided.", LoggingFrequency.AllLogging);
                return;
            }

            includeNestedDirectories = false;
            silentMode = false;
            multiThreaded = Properties.Settings.Default.Multithreaded;
            startMinimized = false;
            threadCount = -1;
            maxParallelInstances = 0;

            for (int i = 0; i < arguments.Length; i++)
            {
                string arg = arguments[i]?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(arg))
                {
                    WriteLine("Empty argument provided.", LoggingFrequency.AllLogging);

                    badOrNoArguments = true;
                    return;
                }

                // Flags

                if (arg.Equals("-startminimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
                else if (arg.Equals("-reopensettings", StringComparison.OrdinalIgnoreCase))
                {
                    reopenSettings = true;
                    break;
                }
                else if (arg.Equals("-reopenoptimizer", StringComparison.OrdinalIgnoreCase))
                {
                    reopenOptimizer = true;
                    break;
                }
                else if (arg.Equals("-apply-optimizations", StringComparison.OrdinalIgnoreCase))
                {
                    applyOptimizations = true;

                    for (int j = i + 1; j < arguments.Length; j++)
                    {
                        string subArg = arguments[j]?.Trim() ?? string.Empty;
                        if (subArg.StartsWith("-profile-mask:", StringComparison.OrdinalIgnoreCase))
                        {
                            ReadOnlySpan<char> maskSpan = subArg.AsSpan("-profile-mask:".Length).Trim();
                            bool parsedMask = maskSpan.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                                ? int.TryParse(maskSpan.Slice(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out optimizationProfileMask)
                                : int.TryParse(maskSpan, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out optimizationProfileMask);

                            if (!parsedMask || optimizationProfileMask < 0 || optimizationProfileMask > 0xFFFF)
                            {
                                applyOptimizations = false;
                                unknownFlagProvided = true;
                                badOrNoArguments = true;
                                return;
                            }

                            hasExplicitOptimizationProfile = true;
                        }
                        else if (subArg.StartsWith("-icon-cache-kb:", StringComparison.OrdinalIgnoreCase))
                        {
                            ReadOnlySpan<char> kbSpan = subArg.AsSpan("-icon-cache-kb:".Length).Trim();
                            if (!int.TryParse(kbSpan, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedKb) || parsedKb <= 0)
                            {
                                applyOptimizations = false;
                                unknownFlagProvided = true;
                                badOrNoArguments = true;
                                return;
                            }

                            optimizationIconCacheKb = Math.Clamp(parsedKb, 1024, 65536);
                        }
                        else
                        {
                            applyOptimizations = false;
                            unknownFlagProvided = true;
                            badOrNoArguments = true;
                            return;
                        }
                    }

                    break;
                }
                else if (arg.Equals("-restore-defaults", StringComparison.OrdinalIgnoreCase))
                {
                    restoreDefaults = true;
                    break;
                }
                else if (arg.Equals("-shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    shutdownExisting = true;
                    break;
                }

                if (arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
                {
                    includeNestedDirectories = true;
                    continue;
                }

                if (arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    silentMode = true;
                    continue;
                }

                // -m = multithread auto
                if (arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                {
                    multiThreaded = true;
                    threadCount = -1; // auto
                    continue;
                }

                // -m:<n>
                if (arg.StartsWith("-m:", StringComparison.OrdinalIgnoreCase))
                {
                    multiThreaded = true;

                    ReadOnlySpan<char> span = arg.AsSpan(3);
                    if (int.TryParse(span, out int n))
                        threadCount = n;

                    continue;
                }

                // -m<n>  (e.g., -m3, -m12...)
                if (arg.StartsWith("-m", StringComparison.OrdinalIgnoreCase) && arg.Length > 2 && char.IsDigit(arg[2]))
                {
                    multiThreaded = true;

                    ReadOnlySpan<char> span = arg.AsSpan(2);
                    if (int.TryParse(span, out int n))
                        threadCount = n;

                    continue;
                }

                // -maxinstances:<n> or -p:<n>
                if (arg.StartsWith("-maxinstances:", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> span = arg.AsSpan("-maxinstances:".Length);
                    if (int.TryParse(span, out int maxInst) && maxInst > 0)
                        maxParallelInstances = maxInst;

                    continue;
                }

                if (arg.StartsWith("-p:", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> span = arg.AsSpan("-p:".Length);
                    if (int.TryParse(span, out int maxInst) && maxInst > 0)
                        maxParallelInstances = maxInst;

                    continue;
                }

                // Paths

                if (TryAddPath(arg))
                {
                    continue;
                }

                // Legacy comma-separated paths fallback:
                // allows C:\,D:\ but does not break real paths that contain commas.
                if (TryAddCommaSeparatedPaths(arg))
                {
                    continue;
                }

                string trimmedArg = arg.Trim().Trim('"');
                if (trimmedArg.StartsWith("-", StringComparison.Ordinal))
                {
                    WriteLine("Unrecognized CLI flag: " + trimmedArg, LoggingFrequency.AllLogging);
                    unknownFlagProvided = true;
                    badOrNoArguments = true;
                    return;
                }

                WriteLine("Invalid path argument: " + arg, LoggingFrequency.AllLogging);

                invalidPathProvided = true;
                invalidPath = arg.Trim().Trim('"');
                badOrNoArguments = true;
                return;
            }

            if (paths.Count == 0)
                badOrNoArguments = true;
        }

        private bool TryAddPath(string rawPath)
        {
            string cleaned = rawPath.Trim().Trim('"');

            string normalized = NormalizePath(cleaned);
            if (normalized == null)
                return false;

            WriteLine("Valid path: " + normalized, LoggingFrequency.PreloaderLogging);
            paths.Add(normalized);
            return true;
        }

        private bool TryAddCommaSeparatedPaths(string rawArg)
        {
            if (!rawArg.Contains(','))
                return false;

            string[] pathParts = rawArg.Split([','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (pathParts.Length <= 1)
                return false;

            List<string> normalizedPaths = new List<string>();

            foreach (string part in pathParts)
            {
                string cleaned = part.Trim().Trim('"');

                string normalized = NormalizePath(cleaned);
                if (normalized == null)
                    return false;

                normalizedPaths.Add(normalized);
            }

            foreach (string normalized in normalizedPaths)
            {
                WriteLine("Valid path: " + normalized, LoggingFrequency.PreloaderLogging);
                paths.Add(normalized);
            }

            return true;
        }

        public static string NormalizePath(string p)
        {
            return NetworkPathResolver.NormalizeAndResolvePath(p);
        }
    }
}
