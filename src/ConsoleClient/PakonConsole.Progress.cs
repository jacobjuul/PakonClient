using System;
using System.Collections.Generic;
using System.Threading;
using PakonLib;
using PakonLib.Enums;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private void WaitForCompletion(string operationName)
        {
            WaitForCompletion(operationName, TimeSpan.Zero);
        }

        private void WaitForCompletion(string operationName, TimeSpan timeout)
        {
            var started = DateTime.UtcNow;
            while (true)
            {
                if (callbackException != null)
                {
                    throw callbackException;
                }

                WorkerThreadProgress progress;
                WorkerThreadOperation operation;
                int status;
                lock (progressLock)
                {
                    progress = lastProgress;
                    operation = lastOperation;
                    status = lastStatus;
                }

                if (progress == WorkerThreadProgress.ProgressComplete)
                {
                    return;
                }

                if (timeout > TimeSpan.Zero && DateTime.UtcNow - started >= timeout)
                {
                    throw new CommandException(
                        "Timed out waiting for TLX to " + operationName + " after " +
                        (int)timeout.TotalSeconds + " seconds. Last callback was " +
                        operation + " with " + FormatCallbackStatus(operation, progress, status) + ".");
                }

                if (verbose)
                {
                    Console.WriteLine("{0}: {1} ({2})", operationName, FormatCallbackStatus(operation, progress, status), operation);
                }

                Thread.Sleep(150);
            }
        }

        private void ResetProgress()
        {
            ResetProgress(WorkerThreadOperation.TlxProgress);
        }

        private void ResetProgress(WorkerThreadOperation expectedOperation)
        {
            lock (progressLock)
            {
                lastOperation = expectedOperation;
                lastProgress = WorkerThreadProgress.Initialize;
                lastStatus = WorkerThreadProgress.InitializeValue;
                callbackException = null;
            }
        }

        private void OnScanProgress(WorkerThreadOperation operation, int status)
        {
            UpdateProgress(operation, status, "scan");
        }

        private void OnSaveProgress(WorkerThreadOperation operation, int status)
        {
            UpdateProgress(operation, status, "save");
        }

        private void OnHardwareProgress(WorkerThreadOperation operation, int status)
        {
            UpdateProgress(operation, status, "hardware");
        }

        private void OnTlxError(WorkerThreadOperation operation, int status)
        {
            if (status == 0)
            {
                if (verbose)
                {
                    Console.WriteLine("tlx event: {0} - no error", operation);
                }

                return;
            }

            var errorCode = ErrorCode.FromValue(status);
            lock (progressLock)
            {
                lastOperation = operation;
                lastStatus = status;
                callbackException = new InvalidOperationException(operation + ": " + errorCode);
            }
        }

        private void UpdateProgress(WorkerThreadOperation operation, int status, string source)
        {
            var progress = WorkerThreadProgress.FromValue(status);
            lock (progressLock)
            {
                lastOperation = operation;
                lastProgress = progress;
                lastStatus = status;
            }

            if (verbose)
            {
                Console.WriteLine("{0} event: {1} - {2}", source, operation, FormatCallbackStatus(operation, progress, status));
            }
        }

        private static string FormatCallbackStatus(WorkerThreadOperation operation, WorkerThreadProgress progress, int status)
        {
            switch (operation)
            {
                case WorkerThreadOperation.HardwareProgress:
                case WorkerThreadOperation.HardwareError:
                    return "hardware status " + FormatHardwareStatus(status);
                case WorkerThreadOperation.HardwareApsProgress:
                case WorkerThreadOperation.HardwareApsError:
                    return "APS hardware status " + FormatApsHardwareStatus(status);
                default:
                    return progress.ToString();
            }
        }

        private static string FormatHardwareStatus(int status)
        {
            var flags = new List<string>();
            AddFlag(flags, status, 1073741824, "film sense entry");
            AddFlag(flags, status, int.MinValue, "film sense exit");
            AddFlag(flags, status, 1, "host board fault");
            AddFlag(flags, status, 2, "DX board fault");
            AddFlag(flags, status, 4, "lamp board fault");
            AddFlag(flags, status, 8, "CCD board fault");
            AddFlag(flags, status, 16, "motor board fault");
            AddFlag(flags, status, 256, "lamp warning");
            AddFlag(flags, status, 512, "lamp error");
            AddFlag(flags, status, 1024, "temperature warning");
            AddFlag(flags, status, 2048, "temperature error");
            AddFlag(flags, status, 4096, "lamp burn out");
            AddFlag(flags, status, 8192, "lamp fan warning");
            AddFlag(flags, status, 16384, "lamp fan error");
            AddFlag(flags, status, 262144, "power warning");
            AddFlag(flags, status, 524288, "power error");
            AddFlag(flags, status, 2097152, "CCD stepper indeterminate");
            AddFlag(flags, status, 4194304, "lens stepper indeterminate");
            AddFlag(flags, status, 8388608, "filter wheel indeterminate");
            AddFlag(flags, status, 16777216, "film guide indeterminate");
            AddFlag(flags, status, 33554432, "film in guides error");
            AddFlag(flags, status, 67108864, "blower warning");
            AddFlag(flags, status, 134217728, "cleaning required");
            AddFlag(flags, status, 268435456, "film emulsion down");
            AddFlag(flags, status, 536870912, "film tail first");
            return FormatFlags(status, flags);
        }

        private static string FormatApsHardwareStatus(int status)
        {
            var flags = new List<string>();
            AddFlag(flags, status, 128, "APS cartridge loaded");
            AddFlag(flags, status, 2, "APS board fault");
            AddFlag(flags, status, 256, "APS extract film jam");
            AddFlag(flags, status, 512, "APS scan film jam");
            AddFlag(flags, status, 1024, "APS retract film jam");
            AddFlag(flags, status, 2048, "APS eject button error");
            AddFlag(flags, status, 4096, "APS unprocessed cartridge error");
            AddFlag(flags, status, 8192, "APS cartridge unpacked error");
            AddFlag(flags, status, 16384, "APS park initialization error");
            AddFlag(flags, status, 32768, "APS park error");
            return FormatFlags(status, flags);
        }

        private static void AddFlag(ICollection<string> flags, int status, int flag, string name)
        {
            if ((status & flag) == flag)
            {
                flags.Add(name);
            }
        }

        private static string FormatFlags(int status, ICollection<string> flags)
        {
            return flags.Count == 0
                ? "0x" + status.ToString("X8")
                : string.Join(", ", flags) + " (0x" + status.ToString("X8") + ")";
        }

        private void ClearErrors(WorkerThreadOperation operation)
        {
            ClearErrors(operation, true);
        }

        private void ClearErrors(WorkerThreadOperation operation, bool print)
        {
            if (scanner == null)
            {
                return;
            }

            var guard = 0;
            while (guard++ < 20)
            {
                var errors = scanner.GetAndClearLastErrorTLX(operation);
                if (print)
                {
                    var errorCode = ErrorCode.FromValue(errors.ReturnValue);
                    Console.WriteLine("TLX error: return={0} ({1}) numbers={2} message={3}",
                        errors.ReturnValue,
                        errorCode,
                        errors.ErrorNumbers,
                        errors.ErrorMessage);
                }

                if (errors.ReturnValue != 25)
                {
                    break;
                }
            }
        }

        private void ClearStartupErrors()
        {
            ClearErrors(WorkerThreadOperation.TlxError, false);
            ClearErrors(WorkerThreadOperation.InitializeError, false);
            ClearErrors(WorkerThreadOperation.HardwareError, false);
            ClearErrors(WorkerThreadOperation.HardwareApsError, false);
            ClearErrors(WorkerThreadOperation.ScanError, false);
            ClearErrors(WorkerThreadOperation.SaveError, false);
        }
    }
}
