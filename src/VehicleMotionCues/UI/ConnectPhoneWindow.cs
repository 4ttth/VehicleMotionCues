using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QRCoder;
using VehicleMotionCues.Sources;

namespace VehicleMotionCues.UI;

/// <summary>Shows a QR code for the phone page plus the steps to get Safari streaming motion.</summary>
public sealed class ConnectPhoneWindow : Window
{
    readonly PhoneBridgeServer _server;
    readonly Image _qr = new() { Width = 240, Height = 240 };
    readonly SelectableTextBlock _url = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock _status = new() { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    readonly DispatcherTimer _timer;

    public ConnectPhoneWindow(PhoneBridgeServer server, WindowIcon? icon)
    {
        _server = server;
        Title = "Connect iPhone — Vehicle Motion Cues";
        Icon = icon;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RenderOptions.SetBitmapInterpolationMode(_qr, BitmapInterpolationMode.None);

        var addresses = NetworkInfo.GetLanAddresses();
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

        root.Children.Add(new TextBlock { Text = "Use your iPhone as the motion sensor", FontSize = 20, FontWeight = FontWeight.SemiBold });

        if (!server.IsRunning)
        {
            root.Children.Add(new TextBlock { Text = "The phone bridge couldn't start. Another app may be using the port.", TextWrapping = TextWrapping.Wrap });
        }
        else if (addresses.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "This laptop isn't on a network. Turn on your iPhone's Personal Hotspot and join it from this laptop, then reopen this window.",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            var picker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var a in addresses)
                picker.Items.Add($"{a.Label} — {a.Address}");
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedIndex >= 0)
                    ShowUrl(server.UrlFor(addresses[picker.SelectedIndex].Address));
            };
            picker.SelectedIndex = 0;

            root.Children.Add(new TextBlock { Text = "Phone and laptop must be on the same Wi-Fi, or the laptop joined to the iPhone's Personal Hotspot.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            if (addresses.Count > 1)
                root.Children.Add(picker);
            else
                ShowUrl(server.UrlFor(addresses[0].Address));

            root.Children.Add(new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = _qr,
            });
            root.Children.Add(_url);
            root.Children.Add(_status);

            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text =
                    "1. Scan with the iPhone Camera and open the link in Safari.\n" +
                    "2. Safari warns \"This Connection Is Not Private\" because the certificate was made by this laptop. " +
                    "Tap Show Details → visit this website → Visit Website. You only do this once.\n" +
                    "3. Tap Start sending motion and allow Motion & Orientation access.\n" +
                    "4. Keep the page open with the screen on. Lay the phone flat with its top toward the front of the car, " +
                    "or stand it up facing you.",
            });
            root.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontSize = 12,
                Text = "If the phone can't load the page, allow Vehicle Motion Cues through Windows Firewall " +
                       "(including Public networks if you're on the iPhone hotspot).",
            });
        }

        var close = new Button { Content = "Done", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90 };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        Content = root;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdateStatus());
        _timer.Start();
        UpdateStatus();
        Closed += (_, _) => _timer.Stop();
    }

    void ShowUrl(string url)
    {
        _url.Text = url;
        using var data = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        _qr.Source = new Bitmap(new MemoryStream(png));
    }

    void UpdateStatus()
    {
        bool receiving = _server.IsReceiving;
        _status.Text = receiving ? "● iPhone connected — receiving motion" : "○ Waiting for iPhone…";
        _status.Foreground = receiving ? new SolidColorBrush(Color.FromRgb(30, 150, 90)) : Brushes.Gray;
    }
}
