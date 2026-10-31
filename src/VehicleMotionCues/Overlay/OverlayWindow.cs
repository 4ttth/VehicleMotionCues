using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using VehicleMotionCues.Platform;

namespace VehicleMotionCues.Overlay;

/// <summary>
/// Transparent, always-on-top, click-through strip along the left or right edge of the primary
/// screen. Two narrow strips cost far less GPU/CPU to composite than one full-screen overlay.
/// </summary>
public sealed class OverlayWindow : Window
{
    readonly bool _rightSide;

    public OverlayWindow(DotsControl dots, bool rightSide)
    {
        _rightSide = rightSide;
        Title = "Vehicle Motion Cues overlay";
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = dots;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToPrimaryScreen();
        ClickThrough.Apply(this);
        Screens.Changed += (_, _) => FitToPrimaryScreen();
    }

    void FitToPrimaryScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
            return;
        var bounds = screen.Bounds;
        int stripPixels = (int)Math.Ceiling(DotsControl.StripWidth * screen.Scaling);
        Position = new PixelPoint(_rightSide ? bounds.Right - stripPixels : bounds.X, bounds.Y);
        Width = stripPixels / screen.Scaling;
        Height = bounds.Height / screen.Scaling;
    }
}
