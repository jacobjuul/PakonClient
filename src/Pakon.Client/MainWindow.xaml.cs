using Microsoft.Win32;
using Pakon.LegacyBridge.Client;
using Pakon.LegacyBridge.Protocol;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pakon.Client;

public partial class MainWindow : Window
{
    private readonly LegacyBridgeClient bridge = new();
    private readonly BridgeProcessHost bridgeHost = new();
    private readonly ObservableCollection<FrameItem> frames = [];
    private readonly DispatcherTimer adjustmentTimer;
    private readonly string sessionDirectory = Path.Combine(Path.GetTempPath(), "Pakon", Guid.NewGuid().ToString("N"));
    private readonly AppSettings settings;
    private CancellationTokenSource? operationCancellation;
    private int page;
    private bool scannerReady;
    private bool updatingSliders;
    private bool closing;

    private bool IsBlackAndWhite => BlackWhiteRadio.IsChecked == true;
    private bool IsColorNegative => ColorNegativeRadio.IsChecked == true;
    private bool IsPositiveSlide => ColorPositiveRadio.IsChecked == true;
    private int PageCount => Math.Max(1, (frames.Count + 8) / 9);
    private IEnumerable<FrameItem> SelectedFrames => frames.Where(x => x.IsSelected);

    public MainWindow()
    {
        InitializeComponent();
        settings = AppSettings.Load();
        Directory.CreateDirectory(sessionDirectory);
        ApplySettings();
        adjustmentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        adjustmentTimer.Tick += async (_, _) =>
        {
            adjustmentTimer.Stop();
            await RefreshSelectedPreviewsAsync();
        };
        Loaded += async (_, _) => await InitializeScannerAsync();
        Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            closing = true;
            operationCancellation?.Cancel();
            SaveSettings();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await bridge.CloseTlxSessionAsync(timeout.Token);
            }
            catch { }
            bridgeHost.Dispose();
            try { Directory.Delete(sessionDirectory, true); } catch { }
            Close();
        };
    }

    private async Task InitializeScannerAsync()
    {
        ShowPage(ScanningPage);
        ProgressTitle.Text = "Starting Pakon";
        ScanStatusText.Text = "Connecting to the scanner…";
        ProgressCancelButton.Visibility = Visibility.Collapsed;
        StartScanButton.IsEnabled = false;
        SetConnection("Initializing scanner…", "#C59134");
        try
        {
            bridgeHost.EnsureStarted();
            await Task.Delay(450);
            var status = await RequireSuccess(bridge.GetTlxSessionStatusAsync());
            if (!status.Values.TryGetValue("state", out var state) || state == "Closed")
                await RequireSuccess(bridge.InitializeTlxSessionAsync());
            await PollUntilReadyAsync("Initializing scanner", CancellationToken.None, TimeSpan.FromMinutes(4));
            scannerReady = true;
            StartScanButton.IsEnabled = true;
            SetConnection("Scanner ready", "#2D7356");
            ShowPage(SetupPage);
        }
        catch (Exception ex)
        {
            scannerReady = false;
            SetConnection("Scanner unavailable", "#B44131");
            ShowPage(SetupPage);
            var retry = MessageBox.Show($"{ex.Message}\n\nCheck power, USB, and the 32-bit Pakon runtime, then choose Retry.",
                "Scanner initialization failed", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (retry == MessageBoxResult.Yes) await InitializeScannerAsync();
        }
    }

    private async void StartScanClicked(object sender, RoutedEventArgs e)
    {
        if (!scannerReady) return;
        SaveSettings();
        ShowPage(ScanningPage);
        ProgressTitle.Text = "Scanning your roll";
        ScanStatusText.Text = "The scanner is finding and capturing each frame.";
        ProgressCancelButton.Visibility = Visibility.Visible;
        operationCancellation = new CancellationTokenSource();
        try
        {
            var filmColor = IsColorNegative
                ? 1
                : ColorPositiveRadio.IsChecked == true
                    ? 2
                    : IceCheckBox.IsChecked == true ? 8 : 4;
            var scanControl = IceCheckBox.IsChecked == true ? 0x08 : 0;
            await RequireSuccess(bridge.ScanRollAsync(2, filmColor, 1, 0, scanControl, operationCancellation.Token));
            await PollUntilReadyAsync("Scanning roll", operationCancellation.Token, TimeSpan.FromMinutes(20));
            ScanStatusText.Text = "Preparing previews…";
            await RequireSuccess(bridge.MoveOldestRollToSaveGroupAsync(operationCancellation.Token));
            await LoadFramesAndPreviewsAsync(operationCancellation.Token);
            ShowReviewPage();
        }
        catch (OperationCanceledException)
        {
            ShowPage(SetupPage);
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (IceCheckBox.IsChecked == true &&
                (message.Contains("COMException: 15", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("invalid parameter", StringComparison.OrdinalIgnoreCase)))
            {
                message += "\n\nThe scanner rejected the Digital ICE acquisition option. Disable Digital ICE and retry the scan.";
            }
            MessageBox.Show(message, "Scan failed", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowPage(SetupPage);
        }
    }

    private async Task LoadFramesAndPreviewsAsync(CancellationToken cancellationToken)
    {
        frames.Clear();
        var response = await RequireSuccess(bridge.GetFramesAsync(cancellationToken));
        var count = int.Parse(response.Values["count"], CultureInfo.InvariantCulture);
        for (var index = 0; index < count; index++)
        {
            var key = $"frame.{index}.";
            var frameNumber = ParseInt(response.Values, key + "frameNumber", index + 1);
            var frameName = response.Values.GetValueOrDefault(key + "frameName", string.Empty);
            var sourcePath = Path.Combine(sessionDirectory, $"preview-source-{index:000}.jpg");
            var render = await RequireSuccess(bridge.RenderFrameToDiskAsync(
                index, sourcePath, NativeSaveControl(includeLowResolution: true), 900, 620, 95, cancellationToken));
            await PollUntilReadyAsync($"Preparing preview {index + 1} of {count}", cancellationToken, TimeSpan.FromMinutes(3));
            sourcePath = render.Values["outputPath"];
            var frame = new FrameItem
            {
                Index = index,
                FrameNumber = frameNumber <= 0 ? index + 1 : frameNumber,
                FrameName = frameName,
                SourcePath = sourcePath
            };
            frame.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FrameItem.IsIncluded)) UpdateReviewSummary();
            };
            var adjustedPath = Path.Combine(
                sessionDirectory,
                $"preview-{frame.Index:000}-{Guid.NewGuid():N}.jpg");
            frame.Preview = await ImageAdjustmentService.CreatePreviewAsync(
                frame,
                !IsPositiveSlide,
                IsBlackAndWhite,
                adjustedPath);
            frames.Add(frame);
        }
        page = 0;
        UpdatePage();
    }

    private async Task PollUntilReadyAsync(string activity, CancellationToken cancellationToken, TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await RequireSuccess(bridge.GetTlxSessionStatusAsync(cancellationToken));
            var state = response.Values.GetValueOrDefault("state", "Unknown");
            var meaning = response.Values.GetValueOrDefault("lastStatusMeaning", string.Empty);
            Dispatcher.Invoke(() =>
            {
                ScanStatusText.Text = string.IsNullOrWhiteSpace(meaning) ? $"{activity}…" : $"{activity} · {meaning}";
                FooterText.Text = $"{activity} · {started.Elapsed:mm\\:ss}";
                if (int.TryParse(response.Values.GetValueOrDefault("lastStatus"), out var progress) && progress is > 0 and <= 100)
                {
                    ScanProgress.IsIndeterminate = false;
                    ScanProgress.Value = progress;
                }
                else
                {
                    ScanProgress.IsIndeterminate = true;
                }
            });
            if (state == "Ready") return;
            if (state == "Faulted")
            {
                var failure = response.Values.GetValueOrDefault("failure");
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(failure)
                    ? "The Pakon scanner reported an error. Check the bridge console for native error details."
                    : failure);
            }
            await Task.Delay(300, cancellationToken);
        }
        throw new TimeoutException($"{activity} did not finish within {timeout.TotalMinutes:0} minutes.");
    }

    private void ShowReviewPage()
    {
        ShowPage(ReviewPage);
        ColorBalancePanel.Visibility = IsBlackAndWhite ? Visibility.Collapsed : Visibility.Visible;
        UpdateReviewSummary();
        FooterText.Text = "Review · Select frames to edit together";
    }

    private void FrameCardClicked(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FrameItem frame) return;
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            var preview = new PreviewWindow(frames.ToArray(), frame, RefreshFramePreviewAsync) { Owner = this };
            preview.ShowDialog();
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            foreach (var item in frames) item.IsSelected = false;
        frame.IsSelected = !frame.IsSelected;
        SyncSlidersFromSelection();
    }

    private void SyncSlidersFromSelection()
    {
        var first = SelectedFrames.FirstOrDefault();
        EditSelectionTitle.Text = first == null ? "Select a frame" :
            SelectedFrames.Skip(1).Any() ? $"{SelectedFrames.Count()} frames selected" : first.DisplayName;
        if (first == null) return;
        updatingSliders = true;
        BrightnessSlider.Value = first.Brightness;
        ContrastSlider.Value = first.Contrast;
        RedSlider.Value = first.RedBalance;
        GreenSlider.Value = first.GreenBalance;
        BlueSlider.Value = first.BlueBalance;
        updatingSliders = false;
    }

    private void AdjustmentChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingSliders || !IsLoaded) return;
        foreach (var frame in SelectedFrames)
        {
            frame.Brightness = BrightnessSlider.Value;
            frame.Contrast = ContrastSlider.Value;
            if (!IsBlackAndWhite)
            {
                frame.RedBalance = RedSlider.Value;
                frame.GreenBalance = GreenSlider.Value;
                frame.BlueBalance = BlueSlider.Value;
            }
        }
        adjustmentTimer.Stop();
        adjustmentTimer.Start();
    }

    private async Task RefreshSelectedPreviewsAsync()
    {
        foreach (var frame in SelectedFrames.ToArray())
            await RefreshFramePreviewAsync(frame);
    }

    private async Task RefreshFramePreviewAsync(FrameItem frame)
    {
        var path = Path.Combine(sessionDirectory, $"adjusted-{frame.Index:000}-{Guid.NewGuid():N}.jpg");
        frame.Preview = await ImageAdjustmentService.CreatePreviewAsync(
            frame,
            !IsPositiveSlide,
            IsBlackAndWhite,
            path);
    }

    private async void RotateLeftClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in SelectedFrames) frame.Rotation -= 90;
        await RefreshSelectedPreviewsAsync();
    }

    private async void RotateRightClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in SelectedFrames) frame.Rotation += 90;
        await RefreshSelectedPreviewsAsync();
    }

    private void IncludeSelectedClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in SelectedFrames) frame.IsIncluded = true;
    }

    private void ExcludeSelectedClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in SelectedFrames) frame.IsIncluded = false;
    }

    private async void ResetAdjustmentsClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in SelectedFrames)
        {
            frame.Brightness = frame.Contrast = frame.RedBalance = frame.GreenBalance = frame.BlueBalance = 0;
            frame.Rotation = 0;
        }
        SyncSlidersFromSelection();
        await RefreshSelectedPreviewsAsync();
    }

    private void SelectAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in frames) frame.IsSelected = true;
        SyncSlidersFromSelection();
    }

    private void ClearSelectionClicked(object sender, RoutedEventArgs e)
    {
        foreach (var frame in frames) frame.IsSelected = false;
        SyncSlidersFromSelection();
    }

    private void PreviousPageClicked(object sender, RoutedEventArgs e) { if (page > 0) { page--; UpdatePage(); } }
    private void NextPageClicked(object sender, RoutedEventArgs e) { if (page + 1 < PageCount) { page++; UpdatePage(); } }

    private void UpdatePage()
    {
        FrameGrid.ItemsSource = frames.Skip(page * 9).Take(9).ToArray();
        PageText.Text = $"{page + 1} / {PageCount}";
        PreviousPageButton.IsEnabled = page > 0;
        NextPageButton.IsEnabled = page + 1 < PageCount;
        UpdateReviewSummary();
    }

    private void UpdateReviewSummary()
    {
        ReviewSummaryText.Text = $"{frames.Count(x => x.IsIncluded)} of {frames.Count} frames included · Page {page + 1} of {PageCount}";
    }

    private void ContinueToSavingClicked(object sender, RoutedEventArgs e)
    {
        if (!frames.Any(x => x.IsIncluded))
        {
            MessageBox.Show("Select at least one frame to save.", "Nothing selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveSummaryText.Text = $"{frames.Count(x => x.IsIncluded)} selected frames are ready. Choose maximum-quality 16-bit PNG or compact JPEG.";
        ShowPage(SavePage);
    }

    private void BackToReviewClicked(object sender, RoutedEventArgs e) => ShowReviewPage();

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for scanned images", Multiselect = false };
        if (dialog.ShowDialog() == true) OutputFolderTextBox.Text = dialog.FolderName;
    }

    private void OpenOutputFolderClicked(object sender, RoutedEventArgs e)
    {
        var directory = OutputFolderTextBox.Text.Trim();
        if (!Directory.Exists(directory)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { directory },
            UseShellExecute = true
        });
    }

    private async void ScanAnotherRollClicked(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        ShowPage(ScanningPage);
        ProgressTitle.Text = "Preparing another roll";
        ScanStatusText.Text = "Clearing the previous scanner session…";
        ProgressCancelButton.Visibility = Visibility.Collapsed;
        ScanProgress.IsIndeterminate = true;
        try
        {
            await RequireSuccess(bridge.CloseTlxSessionAsync());
            await RequireSuccess(bridge.InitializeTlxSessionAsync());
            await PollUntilReadyAsync("Initializing scanner", CancellationToken.None, TimeSpan.FromMinutes(4));
            frames.Clear();
            page = 0;
            FrameGrid.ItemsSource = null;
            SaveProgress.Visibility = Visibility.Collapsed;
            SaveStatusText.Text = "";
            PostSaveActionsPanel.Visibility = Visibility.Collapsed;
            SaveButton.Visibility = Visibility.Visible;
            scannerReady = true;
            StartScanButton.IsEnabled = true;
            SetConnection("Scanner ready", "#2D7356");
            ShowPage(SetupPage);
        }
        catch (Exception ex)
        {
            scannerReady = false;
            StartScanButton.IsEnabled = false;
            SetConnection("Scanner unavailable", "#B44131");
            MessageBox.Show(ex.Message, "Could not prepare another roll", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowPage(SetupPage);
        }
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        var outputDirectory = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            MessageBox.Show("Choose a destination folder.", "Destination required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Directory.CreateDirectory(outputDirectory);
        SaveSettings();
        var included = frames.Where(x => x.IsIncluded).ToArray();
        var usePng = Png16Radio.IsChecked == true;
        SaveButton.IsEnabled = false;
        SaveButton.Visibility = Visibility.Visible;
        SaveProgress.Visibility = Visibility.Visible;
        SaveProgress.Maximum = included.Length;
        SaveProgress.Value = 0;
        PostSaveActionsPanel.Visibility = Visibility.Collapsed;
        try
        {
            for (var position = 0; position < included.Length; position++)
            {
                var frame = included[position];
                SaveStatusText.Text = $"Saving {position + 1} of {included.Length} · {frame.DisplayName}";
                var stem = BuildOutputStem(PrefixTextBox.Text, frame, position + 1);
                var outputPath = UniquePath(outputDirectory, stem, usePng ? ".png" : ".jpg");
                if (usePng)
                    await SavePng16Async(frame, outputPath);
                else
                    await SaveJpegAsync(frame, outputPath);
                SaveProgress.Value = position + 1;
            }
            SaveStatusText.Text = $"Saved {included.Length} frames";
            FooterText.Text = $"Complete · {outputDirectory}";
            SaveButton.Visibility = Visibility.Collapsed;
            PostSaveActionsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Saving failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async Task SavePng16Async(FrameItem frame, string outputPath)
    {
        var rawPath = Path.Combine(sessionDirectory, $"frame-{frame.Index:000}.raw");
        await RequireSuccess(bridge.RenderFrameToRawAsync(frame.Index, rawPath, NativeSaveControl(false) & ~0x04));
        await PollUntilReadyAsync($"Rendering {frame.DisplayName}", CancellationToken.None, TimeSpan.FromMinutes(5));
        var converter = BridgeProcessHost.FindRawConverter();
        var args = new List<string>
        {
            converter, "--input", rawPath, "--output", outputPath, "--format", "png",
            "--gamma", "0.4545454545454545",
            "--contrast", Factor(frame.Contrast), "--saturation", "1",
            "--brightness", Factor(frame.Brightness),
            "--red-balance", Factor(frame.RedBalance), "--green-balance", Factor(frame.GreenBalance),
            "--blue-balance", Factor(frame.BlueBalance), "--rotation", frame.Rotation.ToString(CultureInfo.InvariantCulture)
        };
        if (IsBlackAndWhite) args.Insert(1, "--bw");
        if (!IsPositiveSlide) args.Insert(1, "--invert");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the raw converter.");
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Raw conversion failed: " + error);
    }

    private async Task SaveJpegAsync(FrameItem frame, string outputPath)
    {
        var sourceRequest = Path.Combine(sessionDirectory, $"full-source-{frame.Index:000}.jpg");
        var render = await RequireSuccess(bridge.RenderFrameToDiskAsync(frame.Index, sourceRequest, NativeSaveControl(false) & ~0x04, 0, 0, 100));
        await PollUntilReadyAsync($"Rendering {frame.DisplayName}", CancellationToken.None, TimeSpan.FromMinutes(5));
        await ImageAdjustmentService.SaveJpegAsync(
            render.Values["outputPath"],
            outputPath,
            frame,
            !IsPositiveSlide,
            IsBlackAndWhite);
    }

    private int NativeSaveControl(bool includeLowResolution)
    {
        var value = IsColorNegative ? 0x74 : 0x04;
        if (IceCheckBox.IsChecked == true) value |= 0x80;
        if (includeLowResolution) value |= 0x08;
        return value;
    }

    private static string Factor(double percent) => (1 + percent / 100d).ToString("0.####", CultureInfo.InvariantCulture);

    private static string BuildOutputStem(string prefix, FrameItem frame, int sequence)
    {
        var baseName = !frame.HasUsableDxName
            ? sequence.ToString("000", CultureInfo.InvariantCulture)
            : frame.FrameName.Trim();
        baseName = SanitizeFilePart(Path.GetFileNameWithoutExtension(baseName));
        if (string.IsNullOrWhiteSpace(baseName)) baseName = sequence.ToString("000", CultureInfo.InvariantCulture);
        prefix = SanitizeFilePart(prefix.Trim());
        return string.IsNullOrWhiteSpace(prefix) ? baseName : $"{prefix}-{baseName}";
    }

    private static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        return Regex.Replace(value, $"[{invalid}]+", "-").Trim(' ', '-', '.');
    }

    private static string UniquePath(string directory, string stem, string extension)
    {
        var path = Path.Combine(directory, stem + extension);
        for (var suffix = 2; File.Exists(path); suffix++) path = Path.Combine(directory, $"{stem}-{suffix}{extension}");
        return path;
    }

    private async void CancelScanClicked(object sender, RoutedEventArgs e)
    {
        operationCancellation?.Cancel();
        try { await bridge.CancelScanAsync(); } catch { }
    }

    private void FilmTypeChanged(object sender, RoutedEventArgs e)
    {
        if (ColorBalancePanel != null && ReviewPage.Visibility == Visibility.Visible)
            ColorBalancePanel.Visibility = IsBlackAndWhite ? Visibility.Collapsed : Visibility.Visible;
        UpdateBwIceNotice();
    }

    private void IceSelectionChanged(object sender, RoutedEventArgs e) => UpdateBwIceNotice();

    private void UpdateBwIceNotice()
    {
        if (BwIceNotice != null)
            BwIceNotice.Visibility = IsBlackAndWhite && IceCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ShowPage(UIElement page)
    {
        SetupPage.Visibility = page == SetupPage ? Visibility.Visible : Visibility.Collapsed;
        ScanningPage.Visibility = page == ScanningPage ? Visibility.Visible : Visibility.Collapsed;
        ReviewPage.Visibility = page == ReviewPage ? Visibility.Visible : Visibility.Collapsed;
        SavePage.Visibility = page == SavePage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetConnection(string text, string color)
    {
        ConnectionText.Text = text;
        ConnectionDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        FooterText.Text = text;
    }

    private void ApplySettings()
    {
        OutputFolderTextBox.Text = settings.OutputFolder;
        PrefixTextBox.Text = settings.Prefix;
        ColorNegativeRadio.IsChecked = settings.FilmType == "ColorNegative";
        ColorPositiveRadio.IsChecked = settings.FilmType == "ColorPositive";
        BlackWhiteRadio.IsChecked = settings.FilmType == "BlackAndWhite";
        IceCheckBox.IsChecked = settings.DigitalIce;
        Png16Radio.IsChecked = settings.OutputFormat != "Jpeg";
        JpegRadio.IsChecked = settings.OutputFormat == "Jpeg";
    }

    private void SaveSettings()
    {
        settings.OutputFolder = OutputFolderTextBox.Text.Trim();
        settings.Prefix = PrefixTextBox.Text.Trim();
        settings.FilmType = IsColorNegative ? "ColorNegative" :
            ColorPositiveRadio.IsChecked == true ? "ColorPositive" : "BlackAndWhite";
        settings.DigitalIce = IceCheckBox.IsChecked == true;
        settings.OutputFormat = JpegRadio.IsChecked == true ? "Jpeg" : "Png16";
        try { settings.Save(); } catch { }
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static async Task<BridgeResponse> RequireSuccess(Task<BridgeResponse> request)
    {
        var response = await request;
        if (!response.Succeeded) throw new InvalidOperationException(response.Error ?? "The scanner bridge rejected the request.");
        return response;
    }
}
