using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using QingYi.Models;
using QingYi.Services;
using QingYi.Windows;

namespace QingYi;

public partial class MainWindow : Window, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Icon _appIcon;
    private GlobalInputMonitor _input = null!;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.ToolStripMenuItem _pauseMenu;
    private readonly Font _trayMenuFont;
    private readonly Bitmap _traySettingsIcon;
    private readonly Bitmap _trayPauseIcon;
    private readonly Bitmap _trayResumeIcon;
    private readonly Bitmap _trayWindowIcon;
    private readonly Bitmap _trayExitIcon;
    private readonly DispatcherTimer _geometrySaveTimer;
    private readonly List<ScreenFrame> _screenFrames = new();
    private readonly List<CaptureOverlayWindow> _overlayWindows = new();
    private FloatingToolbarWindow? _textToolbar;
    private FloatingToolbarWindow? _imageToolbar;
    private TranslationResultWindow? _resultWindow;
    private byte[]? _selectedImage;
    private string _selectedText = string.Empty;
    private bool _exiting;
    private bool _disposed;
    private bool _clipboardPending;
    private IDataObject? _clipboardBeforeCopy;
    private uint _clipboardSequenceBefore;
    private System.Windows.Point _lastTextPoint;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _geometrySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _geometrySaveTimer.Tick += (_, _) =>
        {
            _geometrySaveTimer.Stop();
            CaptureWindowGeometry();
            try { SettingsStore.Save(_settings); } catch { }
        };
        ShowInTaskbar = true;
        Width = Math.Clamp(settings.MainWidth, MinWidth, Math.Max(MinWidth, SystemParameters.PrimaryScreenWidth - 32));
        Height = Math.Clamp(settings.MainHeight, MinHeight, Math.Max(MinHeight, SystemParameters.PrimaryScreenHeight - 64));
        if (settings.MainLeft is double savedLeft && settings.MainTop is double savedTop && IsPositionVisible(savedLeft, savedTop, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedLeft;
            Top = savedTop;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        LocationChanged += QueueWindowGeometrySave;
        SizeChanged += QueueWindowGeometrySave;
        ApiUrlBox.Text = settings.ApiUrl;
        ApiKeyBox.Password = SettingsStore.ReadApiKey(settings);
        TextModelBox.Text = settings.TextModel;
        ImageModelBox.Text = settings.ImageModel;
        EnableThinkingToggle.IsChecked = settings.EnableThinking;
        StartWithWindowsToggle.IsChecked = settings.StartWithWindows;
        StartMinimizedToggle.IsChecked = settings.StartMinimized;
        ToolbarScaleSlider.Value = Math.Clamp(settings.ToolbarScale, ToolbarScaleSlider.Minimum, ToolbarScaleSlider.Maximum);
        UpdateToolbarPreview();
        ToolbarScaleSlider.ValueChanged += (_, _) => UpdateToolbarPreview();
        TestButton.ToolTip = "会分别发送一次短文本和内置图片测试，可能产生模型用量。";
        var executablePath = Environment.ProcessPath;
        _appIcon = (executablePath is not null ? System.Drawing.Icon.ExtractAssociatedIcon(executablePath) : null)
            ?? (System.Drawing.Icon)SystemIcons.Application.Clone();

        _trayMenuFont = new Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);
        _traySettingsIcon = TrayMenuGlyphs.Create(TrayMenuGlyph.Settings);
        _trayPauseIcon = TrayMenuGlyphs.Create(TrayMenuGlyph.Pause);
        _trayResumeIcon = TrayMenuGlyphs.Create(TrayMenuGlyph.Resume);
        _trayWindowIcon = TrayMenuGlyphs.Create(TrayMenuGlyph.Window);
        _trayExitIcon = TrayMenuGlyphs.Create(TrayMenuGlyph.Exit);

        _trayMenu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(250, 249, 253),
            ForeColor = System.Drawing.Color.FromArgb(58, 56, 68),
            Font = _trayMenuFont,
            Padding = new Forms.Padding(4, 3, 4, 3),
            ShowImageMargin = true,
            ShowCheckMargin = false,
            DropShadowEnabled = true,
            RenderMode = Forms.ToolStripRenderMode.Professional,
            Renderer = new TrayMenuRenderer()
        };

        var settingsMenu = new Forms.ToolStripMenuItem("打开设置", _traySettingsIcon, (_, _) => ShowFromTray())
        {
            ToolTipText = "调整翻译服务与工具栏外观"
        };
        StyleTrayMenuItem(settingsMenu);
        _trayMenu.Items.Add(settingsMenu);

        _pauseMenu = new Forms.ToolStripMenuItem("暂停翻译监听", _trayPauseIcon)
        {
            ToolTipText = "临时暂停或恢复划词与截图快捷操作"
        };
        StyleTrayMenuItem(_pauseMenu);
        _pauseMenu.Click += (_, _) =>
        {
            _input.IsPaused = !_input.IsPaused;
            UpdateTrayMenuState();
            RunStateText.Text = _input.IsPaused ? "监听已暂停" : "后台待命";
            RunStateDot.Fill = new System.Windows.Media.SolidColorBrush(_input.IsPaused ? System.Windows.Media.Color.FromRgb(226, 168, 72) : System.Windows.Media.Color.FromRgb(72, 169, 119));
            SetStatus(_input.IsPaused ? "翻译监听已暂停" : "翻译监听已恢复", _input.IsPaused);
        };
        _trayMenu.Items.Add(_pauseMenu);

        var restoreMenu = new Forms.ToolStripMenuItem("恢复翻译窗口", _trayWindowIcon, (_, _) => { if (_resultWindow is { IsVisible: false }) _resultWindow.Show(); _resultWindow?.Activate(); })
        {
            ToolTipText = "显示最近打开的翻译结果"
        };
        StyleTrayMenuItem(restoreMenu);
        _trayMenu.Items.Add(restoreMenu);
        _trayMenu.Items.Add(new Forms.ToolStripSeparator { Margin = new Forms.Padding(6, 2, 6, 2) });

        var exitMenu = new Forms.ToolStripMenuItem("退出轻译", _trayExitIcon, (_, _) => ExitApplication())
        {
            ForeColor = System.Drawing.Color.FromArgb(169, 75, 84),
            ToolTipText = "关闭轻译并退出后台监听"
        };
        StyleTrayMenuItem(exitMenu);
        _trayMenu.Items.Add(exitMenu);
        _trayMenu.Opening += (_, _) => UpdateTrayMenuState();
        _trayIcon = new Forms.NotifyIcon { Text = "轻译 · 划词与截图翻译", Icon = _appIcon, ContextMenuStrip = _trayMenu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        try
        {
            _input = new GlobalInputMonitor();
            _input.IsAppChromeAt = IsToolbarAt;
            _input.CaptureRequested += BeginCapture;
            _input.CaptureSelectionStarted += SelectionStarted;
            _input.CaptureSelectionChanged += SelectionChanged;
            _input.CaptureSelectionCompleted += SelectionCompleted;
            _input.CaptureCancelled += CloseCapture;
            _input.TextSelectionRequested += ReadTextSelection;
            _input.NonChromeClick += HideTextToolbar;
        }
        catch (Exception ex)
        {
            _trayIcon.Visible = false;
            MessageBox.Show("Windows 输入监听初始化失败：\n" + ex.Message + "\n\n轻译无法执行划词和截图触发。", "轻译", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void ReadTextSelection(System.Windows.Point point)
    {
        if (_input.IsPaused || _input.IsCaptureActive || _disposed) return;
        var foreground = NativeMethods.GetForegroundWindow();
        _ = NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId) return;

        var physicalPoint = new System.Drawing.Point((int)point.X, (int)point.Y);
        var selected = SelectionReader.TryReadAt(physicalPoint);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            ShowTextToolbar(selected, physicalPoint);
            return;
        }

        BeginClipboardFallback(physicalPoint);
    }

    private void BeginClipboardFallback(System.Drawing.Point point)
    {
        if (_clipboardPending) return;
        try { _clipboardBeforeCopy = Clipboard.GetDataObject(); }
        catch { _clipboardBeforeCopy = null; }
        _clipboardSequenceBefore = NativeMethods.GetClipboardSequenceNumber();
        _lastTextPoint = new System.Windows.Point(point.X, point.Y);
        NativeMethods.SendCtrlC();
        _clipboardPending = true;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(110) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _clipboardPending = false;
            try
            {
                var sequenceAfter = NativeMethods.GetClipboardSequenceNumber();
                var copiedText = sequenceAfter != _clipboardSequenceBefore && Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText).Trim()
                    : string.Empty;

                // Restore only if the clipboard is still exactly the one produced by this copy.
                // A user's newer clipboard write always wins this comparison.
                if (sequenceAfter != _clipboardSequenceBefore && NativeMethods.GetClipboardSequenceNumber() == sequenceAfter)
                {
                    if (_clipboardBeforeCopy is null) Clipboard.Clear();
                    else Clipboard.SetDataObject(_clipboardBeforeCopy, true);
                }
                if (copiedText.Length is > 0 and <= 12000 && !_input.IsCaptureActive)
                    ShowTextToolbar(copiedText, new System.Drawing.Point((int)_lastTextPoint.X, (int)_lastTextPoint.Y));
            }
            catch { }
            finally { _clipboardBeforeCopy = null; }
        };
        timer.Start();
    }

    private void ShowTextToolbar(string text, System.Drawing.Point point)
    {
        HideTextToolbar();
        _selectedText = text;
        _textToolbar = new FloatingToolbarWindow(false, _settings.ToolbarScale);
        _textToolbar.CopyClicked += () =>
        {
            if (ClipboardService.TrySetText(text, out var error))
            {
                _textToolbar?.ShowCopyFeedback(true);
                SetStatus("文本已复制到剪贴板", false);
            }
            else
            {
                _textToolbar?.ShowCopyFeedback(false, error);
                SetStatus(error, true);
            }
        };
        _textToolbar.TranslateClicked += () =>
        {
            var anchor = new System.Drawing.Point(point.X, point.Y);
            _textToolbar?.Close(); _textToolbar = null;
            OpenResult(anchor, text, null);
        };
        _textToolbar.ShowNear(point);
    }

    private void HideTextToolbar()
    {
        if (_textToolbar is null) return;
        _textToolbar.Close();
        _textToolbar = null;
    }

    private bool IsToolbarAt(System.Windows.Point point)
    {
        var p = new System.Drawing.Point((int)point.X, (int)point.Y);
        return (_textToolbar?.IsVisible == true && _textToolbar.GetPhysicalBounds().Contains(p)) ||
               (_imageToolbar?.IsVisible == true && _imageToolbar.GetPhysicalBounds().Contains(p));
    }

    private void BeginCapture()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                CloseCaptureWindows();
                HideTextToolbar();
                var frames = await Task.Run(ImageUtilities.CaptureScreens);
                if (!_input.IsCaptureActive)
                {
                    foreach (var frame in frames) frame.Dispose();
                    return;
                }
                _screenFrames.AddRange(frames);
                var primary = Forms.Screen.PrimaryScreen?.Bounds;
                foreach (var frame in _screenFrames)
                {
                    var overlay = new CaptureOverlayWindow(frame, primary?.Equals(frame.Bounds) == true);
                    _overlayWindows.Add(overlay);
                }
            }
            catch (Exception ex)
            {
                _input.CancelCapture();
                MessageBox.Show("无法截取桌面：" + ex.Message, "轻译", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void SelectionStarted(System.Windows.Point point)
    {
        _selectedImage = null;
        if (_imageToolbar is not null) { _imageToolbar.Close(); _imageToolbar = null; }
        foreach (var overlay in _overlayWindows) overlay.SetSelection(null);
    }

    private void SelectionChanged(System.Drawing.Rectangle selection)
    {
        foreach (var overlay in _overlayWindows) overlay.SetSelection(selection);
    }

    private void SelectionCompleted(System.Drawing.Rectangle selection)
    {
        try
        {
            _selectedImage = ImageUtilities.CropPng(_screenFrames, selection);
            foreach (var overlay in _overlayWindows) overlay.SetSelection(selection);
            if (_imageToolbar is not null) { _imageToolbar.Close(); _imageToolbar = null; }
            _imageToolbar = new FloatingToolbarWindow(true, _settings.ToolbarScale);
            _imageToolbar.CopyClicked += () =>
            {
                try
                {
                    if (_selectedImage is null) throw new InvalidOperationException("截图已失效");
                    if (ClipboardService.TrySetImage(_selectedImage, out var error))
                    {
                        _imageToolbar?.ShowCopyFeedback(true);
                        SetStatus("截图已复制到剪贴板", false);
                    }
                    else
                    {
                        _imageToolbar?.ShowCopyFeedback(false, error);
                        SetStatus(error, true);
                    }
                }
                catch (Exception ex)
                {
                    _imageToolbar?.ShowCopyFeedback(false, ex.Message);
                    SetStatus("复制截图失败：" + ex.Message, true);
                }
            };
            _imageToolbar.TranslateClicked += () =>
            {
                var anchor = new System.Drawing.Point(selection.Right, selection.Top);
                var image = _selectedImage;
                _input.CompleteCapture();
                _imageToolbar?.Close(); _imageToolbar = null;
                CloseCaptureWindows();
                if (image is not null) OpenResult(anchor, string.Empty, image);
            };
            _imageToolbar.ShowNear(new System.Drawing.Point(selection.Right, selection.Top));
        }
        catch (Exception ex)
        {
            CloseCapture();
            MessageBox.Show(ex.Message, "轻译", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseCapture()
    {
        if (_imageToolbar is not null) { _imageToolbar.Close(); _imageToolbar = null; }
        CloseCaptureWindows();
    }

    private void CloseCaptureWindows()
    {
        foreach (var overlay in _overlayWindows.ToArray())
        {
            try { overlay.Close(); } catch { }
        }
        _overlayWindows.Clear();
        foreach (var frame in _screenFrames) frame.Dispose();
        _screenFrames.Clear();
    }

    private void OpenResult(System.Drawing.Point point, string text, byte[]? image)
    {
        if (string.IsNullOrWhiteSpace(SettingsStore.ReadApiKey(_settings)))
        {
            ShowFromTray();
            MessageBox.Show("请先在设置页填写硅基流动 API Key 并保存。", "轻译", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_settings.ApiUrl) || string.IsNullOrWhiteSpace(_settings.TextModel) || (image is not null && string.IsNullOrWhiteSpace(_settings.ImageModel)))
        {
            ShowFromTray();
            MessageBox.Show("请先填写 API URL 和模型 ID。", "轻译", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_resultWindow is not null) { _resultWindow.Close(); _resultWindow = null; }
        _resultWindow = new TranslationResultWindow(_settings, SettingsStore.ReadApiKey(_settings), text, image);
        _resultWindow.Closed += (_, _) => _resultWindow = null;
        if (_settings.ResultLeft is double resultLeft && _settings.ResultTop is double resultTop && IsPositionVisible(resultLeft, resultTop, _resultWindow.Width, _resultWindow.Height))
        {
            _resultWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _resultWindow.Left = resultLeft;
            _resultWindow.Top = resultTop;
        }
        else PlaceResult(_resultWindow, point);
        _resultWindow.Show();
        _resultWindow.Activate();
    }

    private static bool IsPositionVisible(double left, double top, double width, double height)
    {
        var right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        var bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        return left + width > SystemParameters.VirtualScreenLeft + 40 && left < right - 40 &&
               top + height > SystemParameters.VirtualScreenTop + 40 && top < bottom - 40;
    }

    private void QueueWindowGeometrySave(object? sender, EventArgs e)
    {
        if (_disposed || WindowState != WindowState.Normal) return;
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Start();
    }

    private void CaptureWindowGeometry()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.MainWidth = Width;
        _settings.MainHeight = Height;
        _settings.MainLeft = Left;
        _settings.MainTop = Top;
    }

    private void UpdateToolbarPreview()
    {
        var scale = ToolbarScaleSlider.Value;
        ToolbarScaleLabel.Text = $"{scale:P0}";
        ToolbarPreviewHost.Child = FloatingToolbarWindow.CreatePreview(scale);
    }

    private static void PlaceResult(Window window, System.Drawing.Point point)
    {
        var screen = Forms.Screen.FromPoint(point);
        var nativePoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
        _ = NativeMethods.GetDpiForMonitor(NativeMethods.MonitorFromPoint(nativePoint, 2), 0, out var dpiX, out var dpiY);
        var sx = (dpiX == 0 ? 96 : dpiX) / 96d; var sy = (dpiY == 0 ? 96 : dpiY) / 96d;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = Math.Clamp(point.X / sx + 12, screen.Bounds.Left / sx, screen.Bounds.Right / sx - window.Width);
        window.Top = Math.Clamp(point.Y / sy + 12, screen.Bounds.Top / sy, screen.Bounds.Bottom / sy - window.Height);
    }

    public void HideToTray()
    {
        Hide();
        if (!_trayIcon.Visible) _trayIcon.Visible = true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => HideToTray();
    private void CloseToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 1) DragMove();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) HideToTray();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exiting) { e.Cancel = true; HideToTray(); }
    }

    private AppSettings ReadFormSettings()
    {
        _settings.ApiUrl = ApiUrlBox.Text.Trim();
        _settings.TextModel = TextModelBox.Text.Trim();
        _settings.ImageModel = ImageModelBox.Text.Trim();
        _settings.EnableThinking = EnableThinkingToggle.IsChecked == true;
        _settings.ToolbarScale = ToolbarScaleSlider.Value;
        _settings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
        _settings.StartMinimized = StartMinimizedToggle.IsChecked == true;
        SettingsStore.SetApiKey(_settings, ApiKeyBox.Password.Trim());
        return _settings;
    }

    private bool ValidateSettings(bool requireKey, out string error)
    {
        error = string.Empty;
        try { _ = SiliconFlowClient.ResolveEndpoint(ApiUrlBox.Text); }
        catch (Exception ex) { error = ex.Message; return false; }
        if (string.IsNullOrWhiteSpace(TextModelBox.Text) || string.IsNullOrWhiteSpace(ImageModelBox.Text)) { error = "请填写文字和图片翻译模型 ID。"; return false; }
        if (requireKey && string.IsNullOrWhiteSpace(ApiKeyBox.Password)) { error = "请填写硅基流动 API Key。"; return false; }
        return true;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSettings(true, out var error)) { SetStatus(error, true); return; }
        try
        {
            CaptureWindowGeometry();
            _geometrySaveTimer.Stop();
            SettingsStore.Save(ReadFormSettings());
            SetStatus("设置已保存；划词和截图翻译已就绪", false);
        }
        catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSettings(true, out var error)) { SetStatus(error, true); return; }
        TestButton.IsEnabled = false; SaveButton.IsEnabled = false;
        StatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 172, 72));
          StatusText.Text = "正在测试文字模型…";
        var sampleText = new StringBuilder(); var sampleImage = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var client = new SiliconFlowClient();
        try
        {
            var url = ApiUrlBox.Text.Trim(); var key = ApiKeyBox.Password.Trim();
            var textModel = TextModelBox.Text.Trim(); var imageModel = ImageModelBox.Text.Trim();
            var enableThinking = EnableThinkingToggle.IsChecked == true;
              var textTimer = Stopwatch.StartNew();
              await client.TranslateTextAsync(url, key, textModel, "Simplified Chinese", "Hello world. This is a short connection test.", piece => sampleText.Append(piece), timeout.Token,
                  () => StatusText.Text = "文字模型已连通，正在生成…", enableThinking);
              var textElapsed = textTimer.Elapsed.TotalSeconds;
              StatusText.Text = $"文字 {textElapsed:0.0}s；正在测试图片模型…";
              var imageTimer = Stopwatch.StartNew();
              await client.TranslateImageAsync(url, key, imageModel, "English", ImageUtilities.CreateTestImage(), piece => sampleImage.Append(piece), timeout.Token,
                  () => StatusText.Text = "图片模型已连通，正在生成…", enableThinking);
              var imageElapsed = imageTimer.Elapsed.TotalSeconds;
              SetStatus(sampleText.Length > 0 && sampleImage.Length > 0
                  ? $"连接成功 · 文字 {textElapsed:0.0}s / 图片 {imageElapsed:0.0}s"
                  : "接口已响应，但有模型未返回内容", sampleText.Length == 0 || sampleImage.Length == 0);
        }
        catch (Exception ex) { SetStatus("连接失败：" + ex.Message, true); }
        finally { TestButton.IsEnabled = true; SaveButton.IsEnabled = true; }
    }

    private void SetStatus(string text, bool error)
    {
        StatusText.Text = text;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(184, 67, 67) : System.Windows.Media.Color.FromRgb(112, 116, 125));
        StatusDot.Fill = new System.Windows.Media.SolidColorBrush(error ? System.Windows.Media.Color.FromRgb(220, 80, 80) : System.Windows.Media.Color.FromRgb(88, 181, 125));
    }

    private static void StyleTrayMenuItem(Forms.ToolStripItem item)
    {
        item.AutoSize = false;
        item.Size = new System.Drawing.Size(210, 29);
        item.Padding = new Forms.Padding(7, 2, 10, 2);
        item.Margin = new Forms.Padding(2, 1, 2, 1);
        item.ImageAlign = ContentAlignment.MiddleCenter;
        item.TextAlign = ContentAlignment.MiddleLeft;
        item.TextImageRelation = Forms.TextImageRelation.ImageBeforeText;
        item.ImageScaling = Forms.ToolStripItemImageScaling.SizeToFit;
    }

    private void UpdateTrayMenuState()
    {
        if (_input is null || _pauseMenu is null) return;
        var paused = _input.IsPaused;
        _pauseMenu.Text = paused ? "恢复翻译监听" : "暂停翻译监听";
        _pauseMenu.Image = paused ? _trayResumeIcon : _trayPauseIcon;
    }

    private void ExitApplication()
    {
        _exiting = true;
        _geometrySaveTimer.Stop();
        CaptureWindowGeometry();
        try { SettingsStore.Save(_settings); } catch { }
        _input.CancelCapture();
        CloseCaptureWindows();
        Close();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _geometrySaveTimer.Stop();
        try { _input.CancelCapture(); _input.Dispose(); } catch { }
        CloseCaptureWindows();
        try { _textToolbar?.Close(); _imageToolbar?.Close(); _resultWindow?.Close(); } catch { }
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
        _traySettingsIcon.Dispose();
        _trayPauseIcon.Dispose();
        _trayResumeIcon.Dispose();
        _trayWindowIcon.Dispose();
        _trayExitIcon.Dispose();
        _trayMenuFont.Dispose();
        _appIcon.Dispose();
    }
}
