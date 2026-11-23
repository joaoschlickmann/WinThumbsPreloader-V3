#nullable enable
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using WinThumbsPreloader.Properties;
using static WinThumbsPreloader.Logger;
using TaskSchedulerTask = Microsoft.Win32.TaskScheduler.Task;
using Trigger = Microsoft.Win32.TaskScheduler.Trigger;

namespace WinThumbsPreloader.Services
{
    public sealed class ScheduleDriveInfo
    {
        public string Letter { get; init; } = string.Empty;
        public string Root { get; init; } = string.Empty;
        public bool IsMounted { get; init; }
        public string DriveType { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }

    public sealed class ScheduleState
    {
        public bool TaskExists { get; init; }
        public bool Enabled { get; init; }
        public string Frequency { get; init; } = "Day";
        public int Hour { get; init; }
        public int Minute { get; init; }
        public bool OnIdle { get; init; }
        public bool Multithreaded { get; init; }
        public int Threads { get; init; }
        public IReadOnlyList<string> Drives { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> CustomPaths { get; init; } = Array.Empty<string>();
        public string LastRunTimeText { get; init; } = string.Empty;
        public string NextRunTimeText { get; init; } = string.Empty;
        public string StatusMessage { get; init; } = string.Empty;
        public bool DriveSelectionEnabled { get; init; }
        public string DriveSortChoice { get; init; } = "Vertical";
        public bool TwentyFourHourMode { get; init; }
        public bool DetailedTimeMode { get; init; }
        public IReadOnlyList<ScheduleDriveInfo> AvailableDrives { get; init; } = Array.Empty<ScheduleDriveInfo>();
    }

    public sealed class ScheduleSaveRequest
    {
        public string Frequency { get; init; } = "Day";
        public int Hour { get; init; }
        public int Minute { get; init; }
        public bool OnIdle { get; init; }
        public bool Enabled { get; init; } = true;
        public bool Multithreaded { get; init; } = true;
        public int Threads { get; init; }
        public string[] Drives { get; init; } = Array.Empty<string>();
        public string[] CustomPaths { get; init; } = Array.Empty<string>();
        public string? Description { get; init; }
    }

    public sealed class ScheduleActionResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = string.Empty;
        public ScheduleState? State { get; init; }
    }

    [SupportedOSPlatform("windows")]
    public sealed class ScheduleService
    {
        public const string TaskName = ScheduleForm.TaskName;
        private const string DefaultTaskDescription = "Runs WinThumbsPreloader based on user's schedule";
        private const int SW_MAXIMIZE = 3;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public ScheduleState GetState(string? statusOverride = null)
        {
            bool driveSelectionEnabled = Settings.Default.DriveSelectionEnabled;
            string driveSortChoice = string.Equals(Settings.Default.DriveSortChoice, "Horizontal", StringComparison.OrdinalIgnoreCase)
                ? "Horizontal"
                : "Vertical";
            bool twentyFourHourMode = Settings.Default.TwentyFourHourMode;
            bool detailedTimeMode = Settings.Default.DetailedTimeMode;

            IReadOnlyList<ScheduleDriveInfo> availableDrives = BuildAvailableDrives(driveSortChoice);

            bool taskExists = false;
            bool enabled = false;
            string frequency = "Day";
            int hour = 3;
            int minute = 0;
            bool onIdle = false;
            bool multithreaded = Settings.Default.Multithreaded;
            int threads = Math.Max(0, Settings.Default.ThreadCount);
            var drives = new List<string>();
            var customPaths = new List<string>();
            string lastRunTimeText = "The task has not been scheduled yet.";
            string nextRunTimeText = string.Empty;
            string statusMessage = statusOverride ?? string.Empty;

            try
            {
                using var ts = new TaskService();
                TaskSchedulerTask? task = ts.GetTask(TaskName);
                if (task != null)
                {
                    taskExists = true;
                    enabled = task.Enabled;
                    onIdle = task.Definition.Triggers.Any(t => t.TriggerType == TaskTriggerType.Idle);

                    string arguments = (task.Definition.Actions.FirstOrDefault() as ExecAction)?.Arguments ?? string.Empty;
                    ParseArguments(arguments, out multithreaded, out threads, drives, customPaths, driveSelectionEnabled);

                    var triggers = task.Definition.Triggers;
                    if (triggers.OfType<DailyTrigger>().Any(t => t.Repetition != null && t.Repetition.Interval.TotalHours == 1))
                        frequency = "Hour";
                    else if (triggers.OfType<DailyTrigger>().Any())
                        frequency = "Day";
                    else if (triggers.OfType<WeeklyTrigger>().Any())
                        frequency = "Week";
                    else if (triggers.OfType<MonthlyTrigger>().Any())
                        frequency = "Month";
                    else
                        frequency = string.Empty;

                    Trigger? timeTrigger = triggers.FirstOrDefault(t => t is DailyTrigger or WeeklyTrigger or MonthlyTrigger);
                    if (timeTrigger != null)
                    {
                        hour = timeTrigger.StartBoundary.Hour;
                        minute = timeTrigger.StartBoundary.Minute;
                    }

                    if (task.LastTaskResult != 0x41303)
                        lastRunTimeText = $"Last run time: {task.LastRunTime.ToString("g", CultureInfo.InvariantCulture)}";
                    else if (!enabled)
                        lastRunTimeText = "The task has not been enabled yet.";
                    else
                        lastRunTimeText = "The task has not run yet.";

                    nextRunTimeText = task.NextRunTime == DateTime.MinValue
                        ? "Next run time: N/A"
                        : $"Next run time: {task.NextRunTime.ToString("g", CultureInfo.InvariantCulture)}";

                    if (string.IsNullOrEmpty(statusMessage))
                        statusMessage = lastRunTimeText;
                }
                else
                {
                    lastRunTimeText = "Task not found.";
                    if (string.IsNullOrEmpty(statusMessage))
                        statusMessage = "Task not found.";
                }
            }
            catch (Exception ex)
            {
                WriteLine("ScheduleService.GetState error: " + ex.Message, LoggingFrequency.GUILogging);
                if (string.IsNullOrEmpty(statusMessage))
                    statusMessage = "Failed to query Windows Task Scheduler: " + ex.Message;
            }

            return new ScheduleState
            {
                TaskExists = taskExists,
                Enabled = enabled,
                Frequency = frequency,
                Hour = hour,
                Minute = minute,
                OnIdle = onIdle,
                Multithreaded = multithreaded,
                Threads = threads,
                Drives = drives,
                CustomPaths = customPaths,
                LastRunTimeText = lastRunTimeText,
                NextRunTimeText = nextRunTimeText,
                StatusMessage = statusMessage,
                DriveSelectionEnabled = driveSelectionEnabled,
                DriveSortChoice = driveSortChoice,
                TwentyFourHourMode = twentyFourHourMode,
                DetailedTimeMode = detailedTimeMode,
                AvailableDrives = availableDrives
            };
        }

        public ScheduleActionResult SetTaskEnabled(bool enabled)
        {
            try
            {
                using var ts = new TaskService();
                TaskSchedulerTask? task = ts.FindTask(TaskName);
                if (task == null)
                {
                    return new ScheduleActionResult
                    {
                        Ok = false,
                        Message = "Scheduled task does not exist yet. Save a task first.",
                        State = GetState("Task not found.")
                    };
                }

                task.Enabled = enabled;
                string msg = enabled ? "Scheduled task enabled." : "Scheduled task disabled.";
                return new ScheduleActionResult
                {
                    Ok = true,
                    Message = msg,
                    State = GetState(msg)
                };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to update scheduled task enabled state: " + ex.Message, LoggingFrequency.GUILogging);
                return new ScheduleActionResult
                {
                    Ok = false,
                    Message = "Failed to update task state: " + ex.Message,
                    State = GetState()
                };
            }
        }

        public ScheduleActionResult SaveTask(ScheduleSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            WriteLine("Saving scheduled task via ScheduleService", LoggingFrequency.GUILogging);

            try
            {
                string freq = (request.Frequency ?? string.Empty).Trim();
                if (freq != "Hour" && freq != "Day" && freq != "Week" && freq != "Month")
                {
                    return new ScheduleActionResult
                    {
                        Ok = false,
                        Message = "Please select a valid schedule frequency (Hour, Day, Week, or Month).",
                        State = GetState()
                    };
                }

                int hour = Math.Clamp(request.Hour, 0, 23);
                int minute = Math.Clamp(request.Minute, 0, 59);

                string? arguments = ConstructArguments(request.Multithreaded, request.Threads, request.Drives, request.CustomPaths);
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    return new ScheduleActionResult
                    {
                        Ok = false,
                        Message = "Please select at least one drive letter or provide a valid path before saving the task.",
                        State = GetState()
                    };
                }

                DateTime now = DateTime.Now;
                DateTime triggerStart = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
                if (triggerStart < now)
                    triggerStart = triggerStart.AddDays(1);

                string exePath = Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;
                string description = string.IsNullOrWhiteSpace(request.Description)
                    ? DefaultTaskDescription
                    : request.Description.Trim();
                if (description.Length > 512)
                    description = description.Substring(0, 512);

                using var ts = new TaskService();
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = description;

                if (freq == "Hour")
                {
                    var dailyTrigger = new DailyTrigger
                    {
                        StartBoundary = triggerStart
                    };
                    dailyTrigger.Repetition.Duration = TimeSpan.FromDays(1);
                    dailyTrigger.Repetition.Interval = TimeSpan.FromHours(1);
                    td.Triggers.Add(dailyTrigger);
                }
                else
                {
                    Trigger trigger = freq switch
                    {
                        "Day" => new DailyTrigger(),
                        "Week" => new WeeklyTrigger(),
                        _ => new MonthlyTrigger()
                    };
                    trigger.StartBoundary = triggerStart;
                    td.Triggers.Add(trigger);
                }

                if (request.OnIdle)
                {
                    td.Triggers.Add(new IdleTrigger());
                }

                td.Actions.Add(new ExecAction(exePath, arguments, null));
                TaskSchedulerTask registered = ts.RootFolder.RegisterTaskDefinition(TaskName, td);
                registered.Enabled = request.Enabled;

                WriteLine("Task saved successfully", LoggingFrequency.GUILogging);
                return new ScheduleActionResult
                {
                    Ok = true,
                    Message = "Task saved successfully.",
                    State = GetState("Task saved successfully.")
                };
            }
            catch (Exception ex)
            {
                WriteLine($"Error saving task: {ex.Message}", LoggingFrequency.GUILogging);
                return new ScheduleActionResult
                {
                    Ok = false,
                    Message = "Task failed to save. If settings are valid, try restarting as admin.",
                    State = GetState("Task failed to save. If settings are valid, try restarting as admin.")
                };
            }
        }

        public ScheduleActionResult DeleteTask()
        {
            WriteLine("Deleting task via ScheduleService", LoggingFrequency.GUILogging);
            try
            {
                using var ts = new TaskService();
                ts.RootFolder.DeleteTask(TaskName);
                WriteLine("Task deleted successfully", LoggingFrequency.GUILogging);
                return new ScheduleActionResult
                {
                    Ok = true,
                    Message = "Task deleted successfully.",
                    State = GetState("Task deleted successfully.")
                };
            }
            catch (Exception ex)
            {
                WriteLine($"Error deleting task: {ex.Message}", LoggingFrequency.GUILogging);
                return new ScheduleActionResult
                {
                    Ok = false,
                    Message = "Failed to delete task. If the task exists, try restarting as admin.",
                    State = GetState("Failed to delete task. If the task exists, try restarting as admin.")
                };
            }
        }

        public ScheduleActionResult SaveDisplayPreferences(
            bool driveSelectionEnabled,
            string? driveSortChoice,
            bool twentyFourHourMode,
            bool detailedTimeMode)
        {
            Settings.Default.DriveSelectionEnabled = driveSelectionEnabled;
            Settings.Default.DriveSortChoice = string.Equals(driveSortChoice, "Horizontal", StringComparison.OrdinalIgnoreCase)
                ? "Horizontal"
                : "Vertical";
            Settings.Default.TwentyFourHourMode = twentyFourHourMode;
            Settings.Default.DetailedTimeMode = detailedTimeMode;
            Settings.Default.Save();

            return new ScheduleActionResult
            {
                Ok = true,
                Message = "Schedule display preferences saved.",
                State = GetState("Schedule display preferences saved.")
            };
        }

        public ScheduleActionResult ResetDisplayPreferences()
        {
            return SaveDisplayPreferences(
                driveSelectionEnabled: true,
                driveSortChoice: "Vertical",
                twentyFourHourMode: false,
                detailedTimeMode: false);
        }

        public ScheduleActionResult OpenWindowsTaskScheduler()
        {
            WriteLine("Opening Task Scheduler", LoggingFrequency.GUILogging);
            var processes = Process.GetProcessesByName("mmc");
            bool taskSchedulerFound = false;
            IntPtr foregroundWindow = GetForegroundWindow();

            try
            {
                foreach (var process in processes)
                {
                    if (process.MainWindowTitle.Contains("Task Scheduler", StringComparison.OrdinalIgnoreCase))
                    {
                        if (process.MainWindowHandle != foregroundWindow || IsIconic(process.MainWindowHandle))
                        {
                            ShowWindowAsync(process.MainWindowHandle, SW_MAXIMIZE);
                            SetForegroundWindow(process.MainWindowHandle);
                        }
                        taskSchedulerFound = true;
                        break;
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }

            try
            {
                if (!taskSchedulerFound)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskschd.msc"),
                        UseShellExecute = true
                    });
                }

                return new ScheduleActionResult { Ok = true, Message = "Task Scheduler opened." };
            }
            catch (Exception ex)
            {
                WriteLine("Failed to open Task Scheduler: " + ex.Message, LoggingFrequency.GUILogging);
                return new ScheduleActionResult { Ok = false, Message = "Failed to open Task Scheduler: " + ex.Message };
            }
        }

        private static IReadOnlyList<ScheduleDriveInfo> BuildAvailableDrives(string driveSortChoice)
        {
            var mountedMap = new Dictionary<char, DriveInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!string.IsNullOrEmpty(d.Name) && char.IsLetter(d.Name[0]))
                    {
                        mountedMap[char.ToUpperInvariant(d.Name[0])] = d;
                    }
                }
            }
            catch
            {
                // Ignore drive enumeration failures
            }

            List<char> alphabet = Enumerable.Range('A', 26).Select(i => (char)i).ToList();
            if (string.Equals(driveSortChoice, "Horizontal", StringComparison.OrdinalIgnoreCase))
            {
                int itemsPerColumn = (int)Math.Ceiling(alphabet.Count / 3.0);
                var horizontal = new List<char>(alphabet.Count);
                for (int i = 0; i < itemsPerColumn; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        int idx = i + j * itemsPerColumn;
                        if (idx < alphabet.Count)
                            horizontal.Add(alphabet[idx]);
                    }
                }
                alphabet = horizontal;
            }

            var list = new List<ScheduleDriveInfo>(alphabet.Count);
            foreach (char letter in alphabet)
            {
                string letterStr = letter.ToString();
                if (mountedMap.TryGetValue(letter, out DriveInfo? info))
                {
                    string volumeLabel = string.Empty;
                    bool isReady = false;
                    try
                    {
                        isReady = info.IsReady;
                        if (isReady)
                            volumeLabel = info.VolumeLabel ?? string.Empty;
                    }
                    catch { }

                    list.Add(new ScheduleDriveInfo
                    {
                        Letter = letterStr,
                        Root = $"{letterStr}:\\",
                        IsMounted = true,
                        DriveType = info.DriveType.ToString(),
                        Label = volumeLabel
                    });
                }
                else
                {
                    list.Add(new ScheduleDriveInfo
                    {
                        Letter = letterStr,
                        Root = $"{letterStr}:\\",
                        IsMounted = false,
                        DriveType = "Unmounted",
                        Label = string.Empty
                    });
                }
            }

            return list;
        }

        internal static void ParseArguments(
            string arguments,
            out bool multithreaded,
            out int threads,
            List<string> drives,
            List<string> customPaths,
            bool driveSelectionEnabled)
        {
            multithreaded = false;
            threads = 0;

            if (string.IsNullOrWhiteSpace(arguments))
                return;

            multithreaded = Regex.IsMatch(arguments, @"(?:^|\s)-m(?:$|\s|:|\d)", RegexOptions.IgnoreCase);
            Match threadMatch = Regex.Match(arguments, @"(?:^|\s)-m:(\d+)", RegexOptions.IgnoreCase);
            if (threadMatch.Success && int.TryParse(threadMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedThreads))
            {
                threads = Math.Max(0, parsedThreads);
            }

            var pathsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string remainingArgs = arguments;

            foreach (Match quotedMatch in Regex.Matches(arguments, "\"([^\"]+)\""))
            {
                string qPath = quotedMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(qPath) && !qPath.StartsWith('-'))
                {
                    if (qPath.Length == 2 && char.IsLetter(qPath[0]) && qPath[1] == ':')
                        qPath += "\\";
                    AddParsedPath(qPath, pathsSet, drives, driveSelectionEnabled);
                }
            }

            remainingArgs = Regex.Replace(remainingArgs, "\"[^\"]+\"", " ");
            var rawDrivePaths = Regex.Split(remainingArgs, @"(?=(?:\b[A-Za-z]:\\)|(?:\\\\[^\\\s]+\\))")
                .Where(p => !string.IsNullOrWhiteSpace(p));

            foreach (string rawPath in rawDrivePaths)
            {
                string cleanedPath = rawPath.Trim().Replace("\n", string.Empty).Trim('"');
                if (!cleanedPath.StartsWith('-'))
                {
                    AddParsedPath(cleanedPath, pathsSet, drives, driveSelectionEnabled);
                }
            }

            customPaths.AddRange(pathsSet);
        }

        private static void AddParsedPath(
            string cleanedPath,
            HashSet<string> customPathsSet,
            List<string> drives,
            bool driveSelectionEnabled)
        {
            if (cleanedPath.Length == 3 && char.IsLetter(cleanedPath[0]) && cleanedPath[1] == ':' && cleanedPath[2] == '\\')
            {
                string driveLetter = char.ToUpperInvariant(cleanedPath[0]).ToString();
                if (driveSelectionEnabled)
                {
                    if (!drives.Contains(driveLetter, StringComparer.OrdinalIgnoreCase))
                        drives.Add(driveLetter);
                }
                else
                {
                    customPathsSet.Add(cleanedPath);
                }
            }
            else if (cleanedPath.Length > 3)
            {
                customPathsSet.Add(cleanedPath);
            }
        }

        internal static string? ConstructArguments(bool multithreaded, int threads, string[]? drives, string[]? customPaths)
        {
            string arguments = "-r -s ";
            if (multithreaded)
            {
                arguments += threads > 0 ? $"-m:{Math.Clamp(threads, 1, 256)} " : "-m ";
            }

            var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (drives != null)
            {
                foreach (string drive in drives)
                {
                    string clean = (drive ?? string.Empty).Trim().TrimEnd(':', '\\', '/');
                    if (clean.Length == 1 && char.IsLetter(clean[0]))
                    {
                        uniquePaths.Add($"{char.ToUpperInvariant(clean[0])}:\\");
                    }
                }
            }

            if (customPaths != null)
            {
                foreach (string raw in customPaths)
                {
                    string sanitized = raw ?? string.Empty;
                    if (sanitized.Any(char.IsControl))
                        continue;

                    string correctedPath = sanitized.Replace("\"", string.Empty).Replace('/', '\\').Trim();
                    if (!string.IsNullOrWhiteSpace(correctedPath) && !correctedPath.StartsWith('-'))
                    {
                        uniquePaths.Add(correctedPath);
                    }
                }
            }

            if (uniquePaths.Count == 0)
                return null;

            var formattedPaths = uniquePaths.Select(p =>
            {
                string trimmed = p.Trim().Trim('"');
                if (!trimmed.Any(char.IsWhiteSpace))
                    return trimmed;

                if (trimmed.Length > 3 && trimmed.EndsWith('\\'))
                    trimmed = trimmed.TrimEnd('\\');

                return $"\"{trimmed}\"";
            });

            arguments += string.Join(" ", formattedPaths);
            return arguments.Trim();
        }
    }
}
