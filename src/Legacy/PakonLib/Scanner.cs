// Pakon.Scanner
using System;
using TLXLib;
using System.Runtime.InteropServices;
using PakonLib.Enums;
using PakonLib.Interfaces;
using PakonLib.Models;

namespace PakonLib 
{
    public class Scanner
    {
        private ScannerScan scannerScan = null;

        private ScannerSave scannerSave = null;

        private ScannerUnsafe scannerUnsafe = new ScannerUnsafe();

        protected TLXMainClass tlx = new TLXMainClass();

        private int tlxCookie = 0;

        private CallBackClient _callbackClient = null;

        private GCHandle gch;

        public ScannerUnsafe Unsafe
        {
            get
            {
                return scannerUnsafe;
            }
        }

        /// <summary>
        /// Provides scanner transport, calibration, and scan-control operations.
        /// The current implementation delegates to TLX/TLA; its normal scan path ultimately uses
        /// the FX35 driver's asynchronous raw-data ring rather than rendered-image buffers.
        /// </summary>
        public PakonLib.Interfaces.IScanPictures Scanning
        {
            get
            {
                return scannerScan;
            }
        }

        [Obsolete("Use Scanning. This legacy name mirrors the TLX COM interface rather than the client API.")]
        public PakonLib.Interfaces.IScanPictures IScan => Scanning;

        /// <summary>
        /// Provides access to rendered scan images and their output metadata.
        /// </summary>
        public PakonLib.Interfaces.IImageRenderer Images
        {
            get
            {
                return scannerSave;
            }
        }

        /// <summary>
        /// Provides extended metadata for framed images.
        /// </summary>
        public PakonLib.Interfaces.ISavePictures3 ImageMetadata
        {
            get
            {
                return scannerSave;
            }
        }

        public event TLXError TlxError;

        public event TLXHardware TlxHardware;

        public event TLXScanProgress TlxScanProgress;

        public event TLXSaveProgress TlxSaveProgress;

        public Scanner()
        {
            //scannerUnsafe.Allocate(10000);
            scannerScan = new ScannerScan(tlx);
            scannerSave = new ScannerSave(tlx, scannerUnsafe);
        }

        public void TLXAwake(WorkerThreadOperation operation, int status)
        {
            switch (operation)
            {
                case WorkerThreadOperation.InitializeError:
                case WorkerThreadOperation.FirmwareUpdateApsError:
                case WorkerThreadOperation.FirmwareUpdateCcdError:
                case WorkerThreadOperation.FirmwareUpdateDxError:
                case WorkerThreadOperation.FirmwareUpdateLampError:
                case WorkerThreadOperation.FirmwareUpdateMotorError:
                case WorkerThreadOperation.FilmTrackTestError:
                case WorkerThreadOperation.DiagnosticsError:
                case WorkerThreadOperation.CorrectionsError:
                case WorkerThreadOperation.ExerciseSteppersError:
                case WorkerThreadOperation.LampWarmUpError:
                case WorkerThreadOperation.AdvanceFilmError:
                case WorkerThreadOperation.PutFilmGuidePositionError:
                case WorkerThreadOperation.PutFilmPressureRollersPositionError:
                case WorkerThreadOperation.Fx35CManualRetractError:
                case WorkerThreadOperation.ScanError:
                case WorkerThreadOperation.ImportFromFileError:
                case WorkerThreadOperation.SaveError:
                case WorkerThreadOperation.TlxError:
                    if (TlxError != null)
                    {
                        TlxError(operation, status);
                    }
                    break;
                case WorkerThreadOperation.HardwareProgress:
                case WorkerThreadOperation.HardwareError:
                case WorkerThreadOperation.HardwareApsProgress:
                case WorkerThreadOperation.HardwareApsError:
                    if (TlxHardware != null)
                    {
                        TlxHardware(operation, status);
                    }
                    break;
                case WorkerThreadOperation.InitializeProgress:
                case WorkerThreadOperation.FirmwareUpdateApsProgress:
                case WorkerThreadOperation.FirmwareUpdateCcdProgress:
                case WorkerThreadOperation.FirmwareUpdateDxProgress:
                case WorkerThreadOperation.FirmwareUpdateLampProgress:
                case WorkerThreadOperation.FirmwareUpdateMotorProgress:
                case WorkerThreadOperation.FilmTrackTestProgress:
                case WorkerThreadOperation.DiagnosticsProgress:
                case WorkerThreadOperation.CorrectionsProgress:
                case WorkerThreadOperation.ExerciseSteppersProgress:
                case WorkerThreadOperation.LampWarmUpProgress:
                case WorkerThreadOperation.AdvanceFilmProgress:
                case WorkerThreadOperation.PutFilmGuidePositionProgress:
                case WorkerThreadOperation.PutFilmPressureRollersPositionProgress:
                case WorkerThreadOperation.Fx35CManualRetractProgress:
                case WorkerThreadOperation.ScanProgress:
                case WorkerThreadOperation.ImportFromFileProgress:
                case WorkerThreadOperation.TlxProgress:
                case WorkerThreadOperation.OrderAnalysisProgress:
                    if (TlxScanProgress != null)
                    {
                        TlxScanProgress(operation, status);
                    }
                    break;
                case WorkerThreadOperation.SaveProgress:
                    switch (status)
                    {
                        case WorkerThreadProgress.ProgressCompleteValue:
                            Images.ClearRenderBuffers();
                            Unsafe.Deallocate();
                            break;
                        default:
                            Unsafe.ProcessBuffer(this);
                            Unsafe.NextBuffer(this);
                            break;
                        case WorkerThreadProgress.InitializeValue:
                        case WorkerThreadProgress.ProgressStartValue:
                            break;
                    }
                    if (TlxSaveProgress != null)
                    {
                        TlxSaveProgress(operation, status);
                    }
                    break;
            }

            return;

        }

        public virtual bool IsClosed()
        {
            return tlxCookie == 0;
        }

        public virtual void Closing()
        {
        }

        public void Shutdown()
        {
            try
            {
                if (scannerScan != null)
                {
                    scannerScan.ScanCancel();
                }
            }
            catch
            {
            }

            try
            {
                if (scannerSave != null)
                {
                    scannerSave.CancelRender();
                    scannerSave.ClearRenderBuffers();
                }
            }
            catch
            {
            }

            try
            {
                CBUnadviseTLX();
            }
            catch
            {
            }

            try
            {
                scannerUnsafe.Deallocate();
            }
            catch
            {
            }

            if (tlx != null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(tlx);
                }
                catch
                {
                }

                tlx = null;
            }

            scannerScan = null;
            scannerSave = null;
            _callbackClient = null;
        }

        public void InitializeTLX(InitializationRequest request)
        {
            int saveToMemoryTimeout = 200000;
            _callbackClient = new CallBackClient(this);
            gch = GCHandle.Alloc(_callbackClient);
            tlxCookie = tlx.CBAdvise(_callbackClient);
            if (tlxCookie == 0)
            {
                throw new ArgumentNullException("No Scanner Detected");
            }
            tlx.InitializeScanner((int)request.NativeValue, saveToMemoryTimeout);
        }

        [Obsolete("Use the InitializationRequest overload to avoid referencing TLX enums directly.")]
        public void InitializeTLX(INITIALIZE_CONTROL_000 iInitializeControl)
        {
            InitializeTLX(InitializationRequest.FromNative(iInitializeControl));
        }

        public ScannerErrorInfo GetAndClearLastErrorTLX(WorkerThreadOperation operation)
        {
            INT_IID_000 shortInterfaceId = INT_IID_000.INT_IID_ITLAMain;
            switch (operation)
            {
                case WorkerThreadOperation.InitializeError:
                case WorkerThreadOperation.FirmwareUpdateApsError:
                case WorkerThreadOperation.FirmwareUpdateCcdError:
                case WorkerThreadOperation.FirmwareUpdateDxError:
                case WorkerThreadOperation.FirmwareUpdateLampError:
                case WorkerThreadOperation.FirmwareUpdateMotorError:
                case WorkerThreadOperation.HardwareError:
                case WorkerThreadOperation.HardwareApsError:
                    shortInterfaceId = INT_IID_000.INT_IID_ITLAMain;
                    break;
                case WorkerThreadOperation.FilmTrackTestError:
                case WorkerThreadOperation.CorrectionsError:
                case WorkerThreadOperation.ExerciseSteppersError:
                case WorkerThreadOperation.LampWarmUpError:
                case WorkerThreadOperation.AdvanceFilmError:
                case WorkerThreadOperation.PutFilmGuidePositionError:
                case WorkerThreadOperation.PutFilmPressureRollersPositionError:
                case WorkerThreadOperation.Fx35CManualRetractError:
                case WorkerThreadOperation.ScanError:
                case WorkerThreadOperation.ImportFromFileError:
                    shortInterfaceId = INT_IID_000.INT_IID_IScanPictures;
                    break;
                case WorkerThreadOperation.SaveError:
                    shortInterfaceId = INT_IID_000.INT_IID_ISavePictures;
                    break;
                case WorkerThreadOperation.TlxError:
                    shortInterfaceId = INT_IID_000.INT_IID_ITLXMain;
                    break;
            }
            string errorMessage = string.Empty;
            string errorNumbers = string.Empty;
            int returnValue;
            try
            {
                returnValue = tlx.GetAndClearLastError((int)shortInterfaceId, ref errorMessage, ref errorNumbers);
            }
            catch (COMException ex) when (TryGetKnownComError(ex, out ErrorCode errorCode))
            {
                returnValue = errorCode.RawValue;
                errorMessage = GetComExceptionMessage(errorCode);
                errorNumbers = ex.Message;
            }

            return new ScannerErrorInfo(errorMessage, errorNumbers, returnValue);
        }

        private static bool TryGetKnownComError(COMException exception, out ErrorCode errorCode)
        {
            if (ErrorCode.TryFromValue(exception.ErrorCode, out errorCode))
            {
                return true;
            }

            return ErrorCode.TryParseComExceptionMessage(exception.Message, out errorCode);
        }

        private static string GetComExceptionMessage(ErrorCode errorCode)
        {
            if (errorCode.ToInterop() == ERROR_CODES_000.EC_ScannerNotInitialized)
            {
                return errorCode.DisplayName + ". TLX has not initialized the scanner. If the Pakon is powered off, the FX35 driver device is not present, so TLX cannot open the scanner.";
            }

            return errorCode.DisplayName + ".";
        }

        public ScannerInitializeWarnings GetInitializeWarnings()
        {
            int initializeWarnings = 0;
            tlx.GetInitializeWarnings(ref initializeWarnings);
            return (ScannerInitializeWarnings)initializeWarnings;
        }

        public void CBUnadviseTLX()
        {
            if (tlxCookie != 0)
            {
                tlx.CBUnadvise(tlxCookie);
                tlxCookie = 0;
                if (gch.IsAllocated)
                {
                    gch.Free();
                }
                _callbackClient = null;
            }
        }
    }

}
