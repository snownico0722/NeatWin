#nullable enable
using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NeatWin.Presentation;

/// <summary>A small, per-window palette; no UI framework, polling or transparent HWND.</summary>
internal sealed class WindowTheme
{
    private readonly Window _window;
    private readonly bool _warm;
    private bool _followSystem = true;
    internal bool IsDark { get; private set; }

    internal WindowTheme(Window window, bool warm = false)
    {
        _window = window;
        _warm = warm;
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/{typeof(WindowTheme).Assembly.GetName().Name};component/Presentation/ScrollBars.xaml", UriKind.Relative),
        });
        window.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        window.SetResourceReference(Window.BackgroundProperty, "PageBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        Apply(SystemDark());
        window.SourceInitialized += OnSourceInitialized;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        window.Closed += (_, _) => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }

    internal void Toggle() { _followSystem = false; Apply(!IsDark); }
    internal void Apply(bool dark)
    {
        IsDark = dark;
        var highContrast = SystemParameters.HighContrast;
        void Brush(string name, string light, string night, Color contrast)
        {
            var color = highContrast ? contrast : (Color)ColorConverter.ConvertFromString(dark ? night : light);
            var brush = new SolidColorBrush(color); brush.Freeze();
            _window.Resources[name] = brush;
        }
        Brush("PageBrush", _warm ? "#F6F3ED" : "#F3F5F9", "#181C23", SystemColors.WindowColor);
        Brush("SurfaceBrush", "#FFFFFF", "#222731", SystemColors.WindowColor);
        Brush("RaisedBrush", _warm ? "#EFEBE4" : "#E9EEF6", "#2D3440", SystemColors.ControlColor);
        Brush("BorderBrush", _warm ? "#DDD7CC" : "#DCE2EB", "#3B4452", SystemColors.WindowTextColor);
        Brush("TextBrush", "#202834", "#E9EEF5", SystemColors.WindowTextColor);
        Brush("MutedBrush", "#606C7C", "#ACB8C8", SystemColors.WindowTextColor);
        Brush("AccentBrush", _warm ? "#956238" : "#285FD3", _warm ? "#E2B687" : "#9AB9FF", SystemColors.HighlightColor);
        Brush("AccentSoftBrush", _warm ? "#F4E9DC" : "#E9F0FF", _warm ? "#3B3027" : "#273752", SystemColors.HighlightColor);
        Brush("OnAccentBrush", "#FFFFFF", "#182334", SystemColors.HighlightTextColor);
        Brush("DangerBrush", "#B33D3D", "#FFAAA4", SystemColors.WindowTextColor);
        UpdateCaption();
    }

    private static bool SystemDark() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (!_window.Dispatcher.HasShutdownStarted)
            _window.Dispatcher.InvokeAsync(() => Apply(_followSystem ? SystemDark() : IsDark));
    }
    private void OnSourceInitialized(object? sender, EventArgs args) => UpdateCaption();
    private void UpdateCaption()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out ClientRect rect);

    // Used by Windows CI. Render the real WPF view; do not install hooks or invoke app actions.
    internal static async System.Threading.Tasks.Task CaptureAsync(Window window, WindowTheme theme, string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var dark in new[] { false, true })
        {
            theme.Apply(dark);
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var dpi = VisualTreeHelper.GetDpi(window);
            GetClientRect(new WindowInteropHelper(window).Handle, out var client);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(client.Right / dpi.DpiScaleX),
                (int)Math.Ceiling(client.Bottom / dpi.DpiScaleY), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, dark ? "dark.png" : "light.png"));
            encoder.Save(stream);
        }
    }
}
