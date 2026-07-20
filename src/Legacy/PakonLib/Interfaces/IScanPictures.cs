using PakonLib.Enums;
using PakonLib.Models;

namespace PakonLib.Interfaces
{
    /// <summary>
    /// Controls scanner operations. The name is retained for compatibility with TLX's COM
    /// interface; prefer <see cref="Scanner.Scanning"/> when accessing it from PakonClient.
    /// </summary>
    public interface IScanPictures
    {
        void FocusCorrection(Resolution resolution, FilmColor filmColor, FilmFormat filmFormat, bool advanceFilm);

        void LightCorrection(Resolution resolution, FilmColor filmColor, FilmFormat filmFormat, bool includeIr);

        void FilmTrackTest(bool adjustPots);

        int FilmTrackTestResults();

        /// <summary>
        /// Starts TLX's native scan pipeline. TLX configures scanner-side parameters, starts the
        /// FX35 asynchronous raw-data ring, and later frames/processes the staged raw stream.
        /// This method does not render output images; use <see cref="IImageRenderer"/> after a scan.
        /// </summary>
        void ScanPictures(
            Resolution resolution,
            FilmColor filmColor,
            FilmFormat filmFormat,
            StripMode stripMode,
            ScanControl scanControl);

        void ScanCancel();

        void AdvanceFilm(int advanceMilliseconds, int advanceSpeed);

        ScannerInfo GetScannerInfo();
    }
}
