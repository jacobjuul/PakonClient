using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Pakon.Client;

public partial class PreviewWindow : Window
{
    private readonly IReadOnlyList<FrameItem> frames;
    private readonly Func<FrameItem, Task> refreshPreview;
    private int index;
    private double zoom = 1;

    public PreviewWindow(IReadOnlyList<FrameItem> frames, FrameItem initialFrame, Func<FrameItem, Task> refreshPreview)
    {
        InitializeComponent();
        this.frames = frames;
        this.refreshPreview = refreshPreview;
        index = Math.Max(0, frames.ToList().IndexOf(initialFrame));
        Loaded += (_, _) =>
        {
            UpdateFrame();
            Focus();
        };
    }

    private FrameItem Current => frames[index];

    private void UpdateFrame()
    {
        PreviewImage.Source = Current.Preview;
        FrameTitle.Text = Current.DisplayName;
        FramePosition.Text = $"Frame {index + 1} of {frames.Count} · {(Current.IsIncluded ? "Included" : "Excluded")}";
        Title = $"{Current.DisplayName} — Pakon preview";
    }

    private void Move(int offset)
    {
        index = (index + offset + frames.Count) % frames.Count;
        UpdateFrame();
        ImageScroller.ScrollToHorizontalOffset(0);
        ImageScroller.ScrollToVerticalOffset(0);
    }

    private async Task RotateAsync(int degrees)
    {
        Current.Rotation += degrees;
        await refreshPreview(Current);
        UpdateFrame();
    }

    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, 0.25, 4);
        PreviewScale.ScaleX = PreviewScale.ScaleY = zoom;
        ZoomText.Text = $"{zoom * 100:0}%";
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left) Move(-1);
        else if (e.Key == Key.Right) Move(1);
        else if (e.Key == Key.L) _ = RotateAsync(-90);
        else if (e.Key == Key.R) _ = RotateAsync(90);
        else if (e.Key is Key.Add or Key.OemPlus) SetZoom(zoom + 0.25);
        else if (e.Key is Key.Subtract or Key.OemMinus) SetZoom(zoom - 0.25);
        else if (e.Key is Key.D0 or Key.NumPad0) SetZoom(1);
        else if (e.Key == Key.Escape) Close();
    }

    private void PreviewImageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(zoom + (e.Delta > 0 ? 0.25 : -0.25));
        e.Handled = true;
    }

    private void PreviousClicked(object sender, RoutedEventArgs e) => Move(-1);
    private void NextClicked(object sender, RoutedEventArgs e) => Move(1);
    private async void RotateLeftClicked(object sender, RoutedEventArgs e) => await RotateAsync(-90);
    private async void RotateRightClicked(object sender, RoutedEventArgs e) => await RotateAsync(90);
    private void ZoomOutClicked(object sender, RoutedEventArgs e) => SetZoom(zoom - 0.25);
    private void ZoomInClicked(object sender, RoutedEventArgs e) => SetZoom(zoom + 0.25);
    private void FitClicked(object sender, RoutedEventArgs e)
    {
        if (PreviewImage.Source is not BitmapSource source || source.PixelWidth == 0 || source.PixelHeight == 0)
        {
            SetZoom(1);
            return;
        }
        var widthScale = Math.Max(0.25, (ImageScroller.ViewportWidth - 70) / source.PixelWidth);
        var heightScale = Math.Max(0.25, (ImageScroller.ViewportHeight - 70) / source.PixelHeight);
        SetZoom(Math.Min(widthScale, heightScale));
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
