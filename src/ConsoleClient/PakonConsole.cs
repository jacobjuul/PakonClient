using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using PakonLib;
using PakonLib.Enums;
using PakonLib.Models;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private readonly object progressLock = new object();
        private readonly object scannerLock = new object();
        private readonly string launchDirectory = Environment.CurrentDirectory;
        private Scanner scanner;
        private ScannerInfo scannerInfo;
        private bool scannerInitialized;
        private WorkerThreadProgress lastProgress = WorkerThreadProgress.Initialize;
        private WorkerThreadOperation lastOperation = WorkerThreadOperation.TlxProgress;
        private int lastStatus = WorkerThreadProgress.InitializeValue;
        private Exception callbackException;
        private bool verbose;
        private string comServerDirectory;
        private string scannerWorkingDirectory;
        private FilmColor? lastScanFilmColor;
        private int initializeTimeoutSeconds = 5;

        public int Run(string[] args)
        {
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                if (args.Length == 0)
                {
                    return Interactive();
                }

                return Execute(args);
            }
            catch (CommandException ex)
            {
                WriteError(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                WriteExceptionError(ex);
                ClearErrors(lastOperation);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                CloseScanner();
            }
        }

        private int Interactive()
        {
            Console.WriteLine("Pakon command line scanner");
            Console.WriteLine("Type 'help' for commands, 'quit' to exit.");

            while (true)
            {
                Console.Write("pakon> ");
                var line = Console.ReadLine();
                if (line == null)
                {
                    return 0;
                }

                var args = SplitCommandLine(line).ToArray();
                if (args.Length == 0)
                {
                    continue;
                }

                if (IsCommand(args[0], "quit", "exit"))
                {
                    return 0;
                }

                try
                {
                    Execute(args);
                }
                catch (CommandException ex)
                {
                    WriteError(ex.Message);
                }
                catch (Exception ex)
                {
                    WriteExceptionError(ex);
                    ClearErrors(lastOperation);
                }
            }
        }

        private int Execute(string[] args)
        {
            var command = args[0].ToLowerInvariant();
            if (IsCommand(command, "help", "-h", "--help"))
            {
                PrintHelp(args.Skip(1).FirstOrDefault());
                return 0;
            }

            var options = OptionSet.Parse(args.Skip(1));
            verbose = options.GetBool("verbose", "v");
            comServerDirectory = options.Get("com-server-dir", comServerDirectory);
            initializeTimeoutSeconds = options.GetInt("init-timeout-seconds", initializeTimeoutSeconds);
            if (initializeTimeoutSeconds <= 0)
            {
                throw new CommandException("Option --init-timeout-seconds expects a positive integer.");
            }

            if (IsCommand(command, "commands"))
            {
                PrintCommandList();
                return 0;
            }

            if (IsCommand(command, "values"))
            {
                PrintValues();
                return 0;
            }

            if (IsCommand(command, "init"))
            {
                EnsureScanner(options.GetBool("percent-progress"));
                PrintInitializeWarnings();
                return 0;
            }

            if (IsCommand(command, "info"))
            {
                PrintScannerInfo(GetScannerInfo());
                return 0;
            }

            if (IsCommand(command, "diagnostics", "diagnose"))
            {
                PrintDiagnostics(options);
                return 0;
            }

            if (IsCommand(command, "warnings"))
            {
                EnsureScanner(false);
                PrintInitializeWarnings();
                return 0;
            }

            if (IsCommand(command, "errors"))
            {
                EnsureScannerSession();
                ClearErrors(ParseWorkerOperation(options.Get("operation", "tlx")));
                return 0;
            }

            if (IsCommand(command, "scan"))
            {
                Scan(options);
                return 0;
            }

            if (IsCommand(command, "scan-save", "workflow"))
            {
                ScanAndSave(options);
                return 0;
            }

            if (IsCommand(command, "focus-correction"))
            {
                FocusCorrection(options);
                return 0;
            }

            if (IsCommand(command, "light-correction"))
            {
                LightCorrection(options);
                return 0;
            }

            if (IsCommand(command, "film-track-test"))
            {
                FilmTrackTest(options);
                return 0;
            }

            if (IsCommand(command, "film-track-results"))
            {
                EnsureScanner(false);
                Console.WriteLine("Film track result: {0}", scanner.IScan.FilmTrackTestResults());
                return 0;
            }

            if (IsCommand(command, "advance"))
            {
                AdvanceFilm(options);
                return 0;
            }

            if (IsCommand(command, "move-oldest"))
            {
                EnsureScanner(false);
                scanner.Images.MoveOldestRollToSaveGroup();
                Console.WriteLine("Moved oldest roll to save group.");
                return 0;
            }

            if (IsCommand(command, "scan-count"))
            {
                PrintScanGroupCount(options);
                return 0;
            }

            if (IsCommand(command, "save-count"))
            {
                PrintSaveGroupCount();
                return 0;
            }

            if (IsCommand(command, "save", "save-disk", "save-memory"))
            {
                Save(options, command);
                return 0;
            }

            if (IsCommand(command, "picture-info"))
            {
                PrintPictureInfo(options);
                return 0;
            }

            if (IsCommand(command, "pictures"))
            {
                ListPictures(options);
                return 0;
            }

            if (IsCommand(command, "put-picture-info"))
            {
                PutPictureInfo(options);
                return 0;
            }

            if (IsCommand(command, "select"))
            {
                PutPictureSelection(options);
                return 0;
            }

            if (IsCommand(command, "framing"))
            {
                PrintFraming(options);
                return 0;
            }

            if (IsCommand(command, "cancel-scan"))
            {
                EnsureScanner(false);
                scanner.IScan.ScanCancel();
                Console.WriteLine("Scan cancel requested.");
                return 0;
            }

            if (IsCommand(command, "cancel-save"))
            {
                EnsureScanner(false);
                scanner.Images.CancelRender();
                Console.WriteLine("Save cancel requested.");
                return 0;
            }

            if (IsCommand(command, "close"))
            {
                CloseScanner();
                Console.WriteLine("Scanner session closed.");
                return 0;
            }

            throw new CommandException("Unknown command '" + command + "'. Type 'help' for usage.");
        }

        private void EnsureScanner(bool percentProgress)
        {
            lock (scannerLock)
            {
                if (scannerInitialized)
                {
                    return;
                }
            }

            EnsureScannerSession();

            ResetProgress(WorkerThreadOperation.InitializeError);

            Console.WriteLine("Initializing TLX...");
            try
            {
                using (StartInitializeWatchdog())
                {
                    ClearStartupErrors();
                    scanner.InitializeTLX(percentProgress ? InitializationRequest.CSharpClientWithPercentProgress : InitializationRequest.CSharpClient);
                    WaitForCompletion("initialize", TimeSpan.FromSeconds(initializeTimeoutSeconds));
                    Console.WriteLine("Initialized.");
                    lock (scannerLock)
                    {
                        scannerInitialized = true;
                    }
                }
            }
            catch
            {
                CloseScanner();
                throw;
            }
        }

        private void EnsureScannerSession()
        {
            lock (scannerLock)
            {
                if (scanner != null)
                {
                    return;
                }

                if (!IsAdministrator())
                {
                    throw new CommandException("This application requires administrator privileges. Restart the console as Administrator.");
                }

                PrepareNativeRuntime();
                ResetProgress(WorkerThreadOperation.TlxError);
                scanner = new Scanner();
                scanner.TlxScanProgress += OnScanProgress;
                scanner.TlxSaveProgress += OnSaveProgress;
                scanner.TlxHardware += OnHardwareProgress;
                scanner.TlxError += OnTlxError;
            }
        }

        private IDisposable StartInitializeWatchdog()
        {
            return new Timer(
                _ =>
                {
                    Console.Error.WriteLine(
                        "ERROR: Timed out waiting for TLX initialization after " +
                        initializeTimeoutSeconds +
                        " seconds. TLX did not return control to the client, so the process is exiting.");
                    Environment.Exit(2);
                },
                null,
                TimeSpan.FromSeconds(initializeTimeoutSeconds + 5),
                Timeout.InfiniteTimeSpan);
        }

        private ScannerInfo GetScannerInfo()
        {
            EnsureScanner(false);
            if (scannerInfo == null)
            {
                scannerInfo = scanner.IScan.GetScannerInfo();
            }

            return scannerInfo;
        }

        private void PrepareNativeRuntime()
        {
            var directory = ResolveComServerDirectory(comServerDirectory);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new CommandException("Could not find the Pakon F-X35 COM SERVER directory. Use --com-server-dir \"C:\\Program Files (x86)\\Pakon\\F-X35 COM SERVER\".");
            }

            if (!SetDllDirectory(directory))
            {
                throw new CommandException("SetDllDirectory failed for '" + directory + "' with Win32 error " + Marshal.GetLastWin32Error() + ".");
            }

            scannerWorkingDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = directory;

            if (verbose)
            {
                Console.WriteLine("Native DLL directory: " + directory);
                Console.WriteLine("TLX working directory: " + Environment.CurrentDirectory);
            }
        }
        private void CloseScanner()
        {
            Scanner scannerToClose = null;
            lock (scannerLock)
            {
                scannerToClose = scanner;
                scanner = null;
                scannerInfo = null;
                scannerInitialized = false;
                lastScanFilmColor = null;
            }

            if (scannerToClose != null)
            {
                scannerToClose.Shutdown();
            }

            if (!string.IsNullOrEmpty(scannerWorkingDirectory))
            {
                Environment.CurrentDirectory = scannerWorkingDirectory;
                scannerWorkingDirectory = null;
            }
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            CloseScanner();
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            CloseScanner();
        }

        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (scanner == null)
            {
                return;
            }

            e.Cancel = true;
            try
            {
                scanner.IScan.ScanCancel();
                scanner.Images.CancelRender();
                Console.WriteLine("Cancel requested. Press Ctrl+C again after the operation stops to exit.");
            }
            catch
            {
                e.Cancel = false;
            }
        }

        private static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string ResolveComServerDirectory(string overrideDirectory)
        {
            if (!string.IsNullOrEmpty(overrideDirectory))
            {
                return Path.GetFullPath(overrideDirectory);
            }

            var registeredPath = ReadRegisteredTlxPath();
            if (!string.IsNullOrEmpty(registeredPath))
            {
                return Path.GetDirectoryName(registeredPath);
            }

            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Pakon",
                "F-X35 COM SERVER");
            return defaultPath;
        }

        private static string ReadRegisteredTlxPath()
        {
            const string clsid = @"CLSID\{EA82986B-E47C-4C0F-97EA-FB50ED216D2E}\InprocServer32";

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
                using (var key = baseKey.OpenSubKey(clsid))
                {
                    return key == null ? null : key.GetValue(null) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private string ResolveUserPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(launchDirectory, path));
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static bool IsCommand(string actual, params string[] expected)
        {
            return expected.Any(x => string.Equals(actual, x, StringComparison.OrdinalIgnoreCase));
        }

        private static void WriteError(string message)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("Error: " + message);
            Console.ForegroundColor = previous;
        }

        private static void WriteExceptionError(Exception exception)
        {
            WriteError(GetHelpfulErrorMessage(exception));
        }

        private static string GetHelpfulErrorMessage(Exception exception)
        {
            if (exception is TypeInitializationException && exception.InnerException != null)
            {
                return "Client configuration error: " + exception.InnerException.Message;
            }

            if (exception is NotSupportedException)
            {
                return "Unsupported option: " + exception.Message;
            }

            return exception.Message;
        }
    }
}
