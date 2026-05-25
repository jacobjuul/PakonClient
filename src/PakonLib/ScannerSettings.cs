using System;
using PakonLib.Enums;
using PakonLib.Models;

namespace PakonLib
{
    public class ScannerSettings
    {
        private readonly ScannerSettingsSave scannerSettingsSave;

        protected ScannerInitializeWarnings initializeWarnings = ScannerInitializeWarnings.Unknown;

        protected ScannerType scannerType = ScannerType.Unknown;

        protected int serialNumber = 0;

        protected ScannerHW135 hardware135 = ScannerHW135.Unknown;

        protected ScannerHW235 hardware235 = ScannerHW235.Unknown;

        protected ScannerHW335 hardware335 = ScannerHW335.Unknown;

        private IntBits capabilities = null;

        public ScannerInitializeWarnings InitializeWarnings => initializeWarnings;

        public ScannerType Type => scannerType;

        public int SerialNumber => serialNumber;

        public ScannerHW135 Hardware135 => hardware135;

        public ScannerHW235 Hardware235 => hardware235;

        public ScannerHW335 Hardware335 => hardware335;

        public bool this[ScannerCapabilities capability]
        {
            get
            {
                if (capabilities == null)
                {
                    return false;
                }

                return capabilities[(int)capability];
            }
        }

        public ScannerSettingsSave Save => scannerSettingsSave;

        public ScannerSettings()
        {
            scannerSettingsSave = new ScannerSettingsSave();
        }

        public virtual void Reset()
        {
            initializeWarnings = ScannerInitializeWarnings.Unknown;
            scannerType = ScannerType.Unknown;
            serialNumber = 0;
            hardware135 = ScannerHW135.Unknown;
            hardware235 = ScannerHW235.Unknown;
            hardware335 = ScannerHW335.Unknown;
            capabilities = null;
        }

        protected void SetCapabilities()
        {
            capabilities = new IntBits();
            if (Type == ScannerType.F235)
            {
                SetCapabilities(
                    ScannerCapabilities.Film24MM16Base,
                    ScannerCapabilities.Film24MM8Base,
                    ScannerCapabilities.Film24MM4Base,
                    ScannerCapabilities.FilmC41,
                    ScannerCapabilities.FilmPositive,
                    ScannerCapabilities.Film24MM,
                    ScannerCapabilities.Film35MM16Base,
                    ScannerCapabilities.Film35MM8Base,
                    ScannerCapabilities.Film35MM4Base,
                    ScannerCapabilities.LampManualControl,
                    ScannerCapabilities.FocusCorrection,
                    ScannerCapabilities.PressureRollers,
                    ScannerCapabilities.MultipleFilmTrack,
                    ScannerCapabilities.FilterWheel);
            }
            else if (Type == ScannerType.F235C)
            {
                SetCapabilities(
                    ScannerCapabilities.Film24MM16Base,
                    ScannerCapabilities.Film24MM8Base,
                    ScannerCapabilities.Film24MM4Base,
                    ScannerCapabilities.FilmC41,
                    ScannerCapabilities.FilmPositive,
                    ScannerCapabilities.Film24MM,
                    ScannerCapabilities.Film24MMCartridge,
                    ScannerCapabilities.Film35MM16Base,
                    ScannerCapabilities.Film35MM8Base,
                    ScannerCapabilities.Film35MM4Base,
                    ScannerCapabilities.LampManualControl,
                    ScannerCapabilities.FocusCorrection,
                    ScannerCapabilities.PressureRollers,
                    ScannerCapabilities.MultipleFilmTrack,
                    ScannerCapabilities.AIDToolkit,
                    ScannerCapabilities.FilterWheel);
            }
            else if (Type == ScannerType.F135 || Type == ScannerType.F135Plus)
            {
                SetCapabilities(
                    ScannerCapabilities.Film35MM16Base,
                    ScannerCapabilities.Film35MM8Base,
                    ScannerCapabilities.Film35MM4Base,
                    ScannerCapabilities.FixedFocalLength);
            }
            else if (Type == ScannerType.F335)
            {
                SetCapabilities(
                    ScannerCapabilities.Film24MM16Base,
                    ScannerCapabilities.Film24MM8Base,
                    ScannerCapabilities.Film24MM4Base,
                    ScannerCapabilities.FilmC41,
                    ScannerCapabilities.FilmPositive,
                    ScannerCapabilities.Film24MM,
                    ScannerCapabilities.Film35MM16Base,
                    ScannerCapabilities.Film35MM8Base,
                    ScannerCapabilities.Film35MM4Base,
                    ScannerCapabilities.FocusCorrection,
                    ScannerCapabilities.PressureRollers,
                    ScannerCapabilities.MultipleFilmTrack);
            }
            else if (Type == ScannerType.F335C)
            {
                SetCapabilities(
                    ScannerCapabilities.Film24MM16Base,
                    ScannerCapabilities.Film24MM8Base,
                    ScannerCapabilities.Film24MM4Base,
                    ScannerCapabilities.FilmC41,
                    ScannerCapabilities.FilmPositive,
                    ScannerCapabilities.Film24MM,
                    ScannerCapabilities.Film24MMCartridge,
                    ScannerCapabilities.Film35MM16Base,
                    ScannerCapabilities.Film35MM8Base,
                    ScannerCapabilities.Film35MM4Base,
                    ScannerCapabilities.FocusCorrection,
                    ScannerCapabilities.PressureRollers,
                    ScannerCapabilities.MultipleFilmTrack);
            }
        }

        private void SetCapabilities(params ScannerCapabilities[] scannerCapabilities)
        {
            foreach (ScannerCapabilities capability in scannerCapabilities)
            {
                capabilities[capability] = true;
            }
        }

        public void SetScannerType(ScannerType newType)
        {
            if (Type == ScannerType.Unknown || Type == ScannerType.F135 || Type == ScannerType.F135Plus)
            {
                if (scannerType != newType)
                {
                    throw new ArgumentException("Scanner type change not allowed");
                }
            }
            else if (Type == ScannerType.F235 || Type == ScannerType.F235C)
            {
                if (scannerType != ScannerType.F235 && scannerType != ScannerType.F235C)
                {
                    throw new ArgumentException("Scanner type change not allowed");
                }
            }
            else if (Type == ScannerType.F335 || Type == ScannerType.F335C)
            {
                if (scannerType != ScannerType.F335 && scannerType != ScannerType.F335C)
                {
                    throw new ArgumentException("Scanner type change not allowed");
                }
            }

            scannerType = newType;
        }

        public void CompleteTLXInitialization(Scanner scanner)
        {
            initializeWarnings = scanner.GetInitializeWarnings();
            ScannerInfo scannerInfo = scanner.IScan.GetScannerInfo();
            scannerType = scannerInfo.ScannerType;
            serialNumber = scannerInfo.ScannerSerialNumber;
            hardware135 = scannerInfo.Hardware135;
            hardware235 = scannerInfo.Hardware235;
            hardware335 = scannerInfo.Hardware335;
            SetCapabilities();
        }

        public void OpenTLX(Scanner scanner)
        {
            scanner.InitializeTLX(InitializationRequest.CSharpClientWithPercentProgress);
        }

        public void CloseTLX(Scanner scanner)
        {
            scanner.CBUnadviseTLX();
        }

        public int GetResolutionHeight(Scanner scanner, Resolution resolution, FilmFormat filmFormat)
        {
            return Global.GetResolutionHeight(Type, resolution, filmFormat);
        }

        public void Scan(Scanner scanner)
        {
            FilmColor filmColor = FilmColor.Negative;
            FilmFormat filmFormat = FilmFormat.Format35mm;
            Resolution resolution = Resolution.Base4;
            StripMode stripMode = StripMode.FullRoll;
            ScanControl scanControl = ScanControl.None;
            if (Type == ScannerType.F135)
            {
                resolution = Resolution.Base4;
            }
            else if (Type == ScannerType.F235 || Type == ScannerType.F235C || Type == ScannerType.F135Plus)
            {
                resolution = Resolution.Base8;
            }
            else if (Type == ScannerType.F335 || Type == ScannerType.F335C)
            {
                resolution = Resolution.Base8;
            }

            scanner.IScan.ScanPictures(resolution, filmColor, filmFormat, stripMode, scanControl);
        }

        public void EjectFilm(Scanner scanner)
        {
            int advanceMilliseconds = 0;
            int advanceSpeed = 0;
            if (Type == ScannerType.F135 || Type == ScannerType.F135Plus)
            {
                advanceMilliseconds = 500;
                advanceSpeed = 1000;
            }
            else if (Type == ScannerType.F235 || Type == ScannerType.F235C)
            {
                advanceMilliseconds = 5000;
                advanceSpeed = 1000;
            }
            else if (Type == ScannerType.F335 || Type == ScannerType.F335C)
            {
                advanceMilliseconds = 5000;
                advanceSpeed = 1000;
            }

            scanner.IScan.AdvanceFilm(advanceMilliseconds, advanceSpeed);
        }
    }
}
