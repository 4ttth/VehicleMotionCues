using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using VehicleMotionCues.Motion;
using VehicleMotionCues.Overlay;
using VehicleMotionCues.Platform;
using VehicleMotionCues.Sources;
using VehicleMotionCues.UI;

namespace VehicleMotionCues;

public sealed class App : Application
{
    const string HotkeyLabel = "Ctrl+Alt+M";

    Settings _settings = null!;
    MotionHub _hub = null!;
    PhoneBridgeServer _server = null!;
    DotsControl _dots = null!;
    OverlayWindow _leftStrip = null!;
    OverlayWindow _rightStrip = null!;
    TrayIcon _tray = null!;
    WindowIcon? _icon;
    DemoSource? _demo;
    ConnectPhoneWindow? _connectWindow;
    DispatcherTimer? _statusTimer;
#if WINDOWS
    LaptopSensorSource? _laptopSensor;
    GlobalHotkey? _hotkey;
#endif

    NativeMenuItem _showItem = null!;
    NativeMenuItem _statusItem = null!;
    NativeMenuItem _demoItem = null!;

    public override void Initialize()
    {
        Name = "Vehicle Motion Cues";
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Cleanup();

            _settings = Settings.Load();
            _hub = new MotionHub();
            _icon = new WindowIcon(AssetLoader.Open(new Uri("avares://VehicleMotionCues/Assets/icon.png")));

            _server = new PhoneBridgeServer(_hub, _settings);
            try
            {
                _server.Start();
                Log.Write($"Phone bridge listening on port {_server.Port}");
            }
            catch (Exception ex)
            {
                // Also surfaced in the tray status and the Connect window.
                Log.Write($"Phone bridge failed: {ex.Message}");
            }

#if WINDOWS
            _laptopSensor = LaptopSensorSource.TryCreate(_hub);
            Log.Write(_laptopSensor is null ? "No laptop accelerometer" : "Laptop accelerometer found");
            _hotkey = new GlobalHotkey(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, 'M', ToggleCues);
            Log.Write($"Hotkey {HotkeyLabel} registered: {_hotkey.IsRegistered} {_hotkey.Error}");
#endif

            _dots = new DotsControl(_hub, _settings);
            _leftStrip = new OverlayWindow(_dots, rightSide: false);
            _rightStrip = new OverlayWindow(new DotsControl(_dots), rightSide: true);
            _leftStrip.Show();
            _rightStrip.Show();
            _dots.Enabled = _settings.ShowCues;

            BuildTray();
            _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateStatus());
            _statusTimer.Start();
            UpdateStatus();

            if (desktop.Args?.Contains("--demo") == true)
                SetDemo(true);

            if (_settings.IsFirstRun && !HasLaptopSensor)
                ShowConnectWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    bool HasLaptopSensor =>
#if WINDOWS
        _laptopSensor is not null;
#else
        false;
#endif

    void BuildTray()
    {
        _showItem = new NativeMenuItem($"Show motion cues ({HotkeyLabel})")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _settings.ShowCues,
        };
        _showItem.Click += (_, _) => ToggleCues();

        _statusItem = new NativeMenuItem("Starting…") { IsEnabled = false };

        var connect = new NativeMenuItem("Connect iPhone…");
        connect.Click += (_, _) => ShowConnectWindow();

        var sensitivity = new NativeMenuItem("Sensitivity") { Menu = new NativeMenu() };
        foreach (var level in new[] { "Low", "Medium", "High" })
        {
            var item = new NativeMenuItem(level)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = _settings.Sensitivity == level,
            };
            item.Click += (_, _) =>
            {
                _settings.Sensitivity = level;
                _settings.Save();
                foreach (var other in sensitivity.Menu.Items.OfType<NativeMenuItem>())
                    other.IsChecked = other == item;
            };
            sensitivity.Menu.Items.Add(item);
        }

        var display = new NativeMenuItem("When to show") { Menu = new NativeMenu() };
        foreach (var (mode, label) in new[] { ("Automatic", "Automatically while moving"), ("Always", "Always") })
        {
            var item = new NativeMenuItem(label)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = _settings.DisplayMode == mode,
            };
            item.Click += (_, _) =>
            {
                _settings.DisplayMode = mode;
                _settings.Save();
                foreach (var other in display.Menu.Items.OfType<NativeMenuItem>())
                    other.IsChecked = other == item;
                _dots.Enabled = _settings.ShowCues; // re-evaluate visibility now
            };
            display.Menu.Items.Add(item);
        }

        var dots = new NativeMenuItem("Dots") { Menu = new NativeMenu() };
        var larger = new NativeMenuItem("Larger dots") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = _settings.LargerDots };
        larger.Click += (_, _) =>
        {
            _settings.LargerDots = !_settings.LargerDots;
            larger.IsChecked = _settings.LargerDots;
            _settings.Save();
            _dots.Repaint();
        };
        var moreDots = new NativeMenuItem("More dots") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = _settings.MoreDots };
        moreDots.Click += (_, _) =>
        {
            _settings.MoreDots = !_settings.MoreDots;
            moreDots.IsChecked = _settings.MoreDots;
            _settings.Save();
            _dots.Repaint();
        };
        dots.Menu.Items.Add(larger);
        dots.Menu.Items.Add(moreDots);

        var direction = new NativeMenuItem("Direction") { Menu = new NativeMenu() };
        var flipX = new NativeMenuItem("Flip left / right") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = _settings.FlipLeftRight };
        flipX.Click += (_, _) =>
        {
            _settings.FlipLeftRight = !_settings.FlipLeftRight;
            flipX.IsChecked = _settings.FlipLeftRight;
            _settings.Save();
        };
        var flipY = new NativeMenuItem("Flip forward / back") { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = _settings.FlipForwardBack };
        flipY.Click += (_, _) =>
        {
            _settings.FlipForwardBack = !_settings.FlipForwardBack;
            flipY.IsChecked = _settings.FlipForwardBack;
            _settings.Save();
        };
        direction.Menu.Items.Add(flipX);
        direction.Menu.Items.Add(flipY);

        _demoItem = new NativeMenuItem("Preview with demo motion") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _demoItem.Click += (_, _) => SetDemo(_demo is null);

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(connect);
        menu.Items.Add(display);
        menu.Items.Add(dots);
        menu.Items.Add(sensitivity);
        menu.Items.Add(direction);
        menu.Items.Add(_demoItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);

        _tray = new TrayIcon
        {
            Icon = _icon,
            ToolTipText = "Vehicle Motion Cues",
            Menu = menu,
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ToggleCues();
        TrayIcon.SetIcons(this, [_tray]);
    }

    void ToggleCues()
    {
        _settings.ShowCues = !_settings.ShowCues;
        _settings.Save();
        _dots.Enabled = _settings.ShowCues;
        _showItem.IsChecked = _settings.ShowCues;
        Log.Write($"Cues {(_settings.ShowCues ? "on" : "off")}");
        UpdateStatus();
    }

    void SetDemo(bool on)
    {
        if (on && _demo is null)
            _demo = new DemoSource(_hub);
        else if (!on && _demo is not null)
        {
            _demo.Dispose();
            _demo = null;
        }
        _hub.DemoEnabled = _demo is not null;
        _demoItem.IsChecked = _demo is not null;
        if (on && !_settings.ShowCues)
            ToggleCues();
        else
            _dots.Enabled = _settings.ShowCues; // wake the loop so the change shows immediately
        UpdateStatus();
    }

    void ShowConnectWindow()
    {
        if (_connectWindow is not null)
        {
            _connectWindow.Activate();
            return;
        }
        _connectWindow = new ConnectPhoneWindow(_server, _icon);
        _connectWindow.Closed += (_, _) => _connectWindow = null;
        _connectWindow.Show();
    }

    void UpdateStatus()
    {
        string status = _hub.Current(out _, out _) switch
        {
            SourceKind.Demo => "Demo motion",
            SourceKind.Phone => "iPhone connected",
            SourceKind.LaptopSensor => "Using laptop motion sensor",
            _ when !_server.IsRunning => "Phone bridge failed to start",
            _ => "Waiting for iPhone…",
        };
        string state = !_settings.ShowCues ? "off" : _dots.IsShowingDots ? "showing" : "waiting for motion";
        _statusItem.Header = status;
        _tray.ToolTipText = $"Vehicle Motion Cues — {state} · {status}";
    }

    void Cleanup()
    {
        _statusTimer?.Stop();
        _demo?.Dispose();
        _server.Dispose();
#if WINDOWS
        _laptopSensor?.Dispose();
        _hotkey?.Dispose();
#endif
        _tray.IsVisible = false;
        _tray.Dispose();
    }
}
