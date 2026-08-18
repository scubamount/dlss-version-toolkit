using System.Windows;

namespace DLSSVersionToolkit;

public partial class MainWindow : Window
{
    // Preferred size when the display can afford it. The window is never allowed to exceed the
    // work area of the monitor it actually opens on.
    private const double PreferredWidth = 1320;
    private const double PreferredHeight = 880;

    // Below this the layout stops being usable (sidebar 264 + the 6-column versions grid).
    // The grid and sidebar both scroll, so a smaller window degrades to scrolling rather than
    // clipping — but we still refuse to open smaller than this unless the screen forces it.
    private const double FloorWidth = 900;
    private const double FloorHeight = 560;

    public MainWindow()
    {
        InitializeComponent();

        // Fit before the window is shown so it never flashes oversized, and re-fit whenever it
        // is moved to a monitor with a different work area or DPI.
        SourceInitialized += (_, _) => FitToWorkArea();
        DpiChanged += (_, _) => FitToWorkArea();
    }

    /// <summary>
    /// Clamp the window to the work area of the monitor it is on.
    ///
    /// Why this is not markup: the XAML previously bound MaxWidth/MaxHeight to
    /// SystemParameters.MaximizedPrimaryScreenWidth/Height via x:Static. That is a one-shot
    /// read of the PRIMARY monitor at load time, so it was wrong in three ways — it ignored
    /// the monitor the window actually opens on (multi-display), it never updated when the
    /// window moved to a different-DPI display, and it did not subtract the taskbar. Worse,
    /// the fixed 1320x880 default plus MinHeight=660 was larger than the work area of several
    /// very common configurations (1920x1080 at 125/150/175% scaling, 1600x900, 1366x768) —
    /// at 1080p/150% the work area is 1280x688, so the 660 minimum plus title bar could not
    /// fit and the bottom of the window (status bar and action buttons) was pushed off-screen
    /// with no way to resize it back.
    ///
    /// SystemParameters.WorkArea is the primary monitor's; for the actual monitor we ask the
    /// window's own screen via its visual bounds intersected against the virtual screen. WPF
    /// has no per-monitor API without WinForms/Win32 interop, so we use the transform-aware
    /// work area when the window is on the primary display and fall back to the virtual-screen
    /// bounds otherwise — both are DIP values, which is what Width/Height expect.
    /// </summary>
    private void FitToWorkArea()
    {
        var (availableWidth, availableHeight) = AvailableWorkArea();

        // Chrome: title bar + borders. SystemParameters gives this in DIPs.
        var chromeHeight = SystemParameters.WindowCaptionHeight +
                           (SystemParameters.ResizeFrameHorizontalBorderHeight * 2);

        // The SCREEN always wins. An earlier version of this used Math.Max(Floor, available),
        // which let the floor exceed the work area and reintroduced the very clipping this
        // method exists to prevent (1920x1080 at 175% has a 1097x589 work area — smaller than
        // the 900x560 floor once chrome is subtracted). The floor is a preference, not a
        // guarantee: when the display cannot afford it, the window shrinks below it and the
        // content scrolls, because a window that does not fit cannot be resized back.
        var maxW = availableWidth;
        var maxH = availableHeight - chromeHeight;

        // Never advertise a minimum the screen cannot satisfy — an unsatisfiable MinHeight is
        // what pushed the status bar off-screen at 1080p/150%.
        MinWidth = Math.Min(FloorWidth, maxW);
        MinHeight = Math.Min(FloorHeight, maxH);

        MaxWidth = maxW;
        MaxHeight = maxH;

        Width = Math.Min(PreferredWidth, maxW);
        Height = Math.Min(PreferredHeight, maxH);

        // Re-centre only if we shrank the window off the edge of the work area.
        if (Left < 0 || Top < 0 || Left + Width > availableWidth || Top + Height > availableHeight)
        {
            Left = Math.Max(0, (availableWidth - Width) / 2);
            Top = Math.Max(0, (availableHeight - Height) / 2);
        }
    }

    private (double Width, double Height) AvailableWorkArea()
    {
        // Primary-monitor work area (taskbar already subtracted), in DIPs.
        var work = SystemParameters.WorkArea;
        var width = work.Width;
        var height = work.Height;

        // If the window sits outside the primary monitor's bounds it is on a secondary display;
        // the virtual screen is then the only WPF-native bound available. Subtract a nominal
        // taskbar allowance so a maximised taskbar does not overlap the status bar.
        var onPrimary = Left >= work.Left - 1 && Top >= work.Top - 1 &&
                        Left < work.Right && Top < work.Bottom;
        if (!onPrimary)
        {
            width = SystemParameters.VirtualScreenWidth;
            height = SystemParameters.VirtualScreenHeight - (work.Top + (SystemParameters.PrimaryScreenHeight - work.Bottom));
            if (height <= 0) height = SystemParameters.VirtualScreenHeight;
        }

        return (width, height);
    }
}
