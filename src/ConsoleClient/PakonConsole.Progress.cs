using System;
using System.Threading;
using PakonLib;
using PakonLib.Enums;

namespace ConsoleClient
{
    internal sealed partial class PakonConsole
    {
        private void WaitForCompletion(string operationName)
        {
            while (true)
            {
                if (callbackException != null)
                {
                    throw callbackException;
                }

                WorkerThreadProgress progress;
                WorkerThreadOperation operation;
                lock (progressLock)
                {
                    progress = lastProgress;
                    operation = lastOperation;
                }

                if (progress == WorkerThreadProgress.ProgressComplete)
                {
                    return;
                }

                if (verbose)
                {
                    Console.WriteLine("{0}: {1} ({2})", operationName, progress, operation);
                }

                Thread.Sleep(150);
            }
        }

        private void ResetProgress()
        {
            lock (progressLock)
            {
                lastProgress = WorkerThreadProgress.Initialize;
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
            var errorCode = ErrorCode.FromValue(status);
            lock (progressLock)
            {
                lastOperation = operation;
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
            }

            if (verbose)
            {
                Console.WriteLine("{0} event: {1} - {2}", source, operation, progress);
            }
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
    }
}
