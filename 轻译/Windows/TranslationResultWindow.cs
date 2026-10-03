using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QingYi.Models;
using QingYi.Services;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace QingYi.Windows;

public sealed class TranslationResultWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _apiKey;
    private readonly string _sourceText;
    private readonly byte[]? _image;
    private readonly SiliconFlowClient _client = new();
    private readonly StringBuilder _streamedText = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _geometrySaveTimer;
    private readonly TextBlock _result;
    private readonly TextBlock _status;
    private readonly ComboBox _language;
    private readonly Border _sourcePanel;
    private readonly TextBlock _sourceTextBlock;
    private readonly Image _sourceImage;
    private readonly Border _dragSurface;
    private Button _stopButton = null!;
    private Popup? _opacityPopup;
    private CancellationTokenSource? _request;
    private string _completedText = string.Empty;
    private bool _pendingRender;
    private bool _isTranslating;
    private bool _isPinned;

    public TranslationResultWindow(AppSettings settings, string apiKey, string sourceText, byte[]? image)
    {
        _settings = settings;
        _apiKey = apiKey;
        _sourceText = sourceText;
        _image = image;
        Title = image is null ? "翻译" : "图片翻译";
        Width = settings.ResultWidth;
        Height = settings.ResultHeight;
        MinWidth = 380;
        MinHeight = 270;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = false;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _renderTimer.Tick += (_, _) => FlushStream();
        _geometrySaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _geometrySaveTimer.Tick += (_, _) =>
        {
            _geometrySaveTimer.Stop();
            CaptureWindowGeometry();
            try { SettingsStore.Save(_settings); } catch { }
        };
        Content = BuildUi(out _dragSurface, out _result, out _status, out _language, out _sourcePanel, out _sourceTextBlock, out _sourceImage);
        Loaded += (_, _) => { DragMoveHeader(); _ = TranslateAsync(); };
        SizeChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal) { _settings.ResultWidth = Width; _settings.ResultHeight = Height; }
            QueueGeometrySave();
        };
        LocationChanged += (_, _) => QueueGeometrySave();
        Closed += (_, _) => { _renderTimer.Stop(); _geometrySaveTimer.Stop(); _request?.Cancel(); _request?.Dispose(); };
        Closed += (_, _) => { CaptureWindowGeometry(); try { SettingsStore.Save(_settings); } catch { } };
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive && !_isPinned && !_language.IsDropDownOpen && _opacityPopup?.IsOpen != true) Close();
        });
        PreviewKeyDown += ResultKeyDown;
    }

    private UIElement BuildUi(out Border dragSurface, out TextBlock result, out TextBlock status, out ComboBox language, out Border sourcePanel, out TextBlock sourceText, out Image sourceImage)
    {
        var shell = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromRgb(229, 226, 240)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(5),
            Effect = new DropShadowEffect { Color = Color.FromRgb(43, 37, 83), BlurRadius = 18, ShadowDepth = 3, Opacity = 0.16 }
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(51) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });

        var header = new Border
        {
            Background = HeaderBrush,
            CornerRadius = new CornerRadius(13, 13, 0, 0),
            Padding = new Thickness(13, 0, 10, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = LogoBrush,
            Child = new TextBlock
            {
                Text = "译",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        headerGrid.Children.Add(brand);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 0) };
        titleStack.Children.Add(new TextBlock
        {
            Text = _image is null ? "翻译结果" : "图片翻译结果",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = _image is null ? "轻译 · 流式译文" : "轻译 · 图中文字",
            FontSize = 10,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);

        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var pin = MakeHeaderButton("⌖", "置顶窗口");
        pin.Click += (_, _) => { _isPinned = !_isPinned; Topmost = _isPinned; pin.Foreground = _isPinned ? AccentBrush : MutedBrush; };
        var opacity = MakeHeaderButton("◐", "窗口透明度");
        var opacitySlider = new Slider { Minimum = 0.35, Maximum = 1, Value = 1, Height = 105, Orientation = Orientation.Vertical, TickFrequency = 0.1, IsSnapToTickEnabled = false };
        opacitySlider.ValueChanged += (_, e) => Opacity = e.NewValue;
        var opacityPopup = new Popup
        {
            PlacementTarget = opacity,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 11, 9, 8),
                Child = opacitySlider,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.18 }
            }
        };
        _opacityPopup = opacityPopup;
        opacity.Click += (_, _) => opacityPopup.IsOpen = !opacityPopup.IsOpen;
        var minimize = MakeHeaderButton("−", "最小化");
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = MakeHeaderButton("×", "关闭");
        close.Click += (_, _) => Close();
        headerActions.Children.Add(pin);
        headerActions.Children.Add(opacity);
        headerActions.Children.Add(minimize);
        headerActions.Children.Add(close);
        Grid.SetColumn(headerActions, 2);
        headerGrid.Children.Add(headerActions);
        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        root.Children.Add(header);
        dragSurface = header;

        var body = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        language = new ComboBox
        {
            Width = 190,
            Height = 36,
            FontSize = 12,
            Foreground = InkBrush,
            Background = ControlBrush,
            BorderBrush = LightBorderBrush,
            BorderThickness = new Thickness(1),
            SelectedIndex = 0,
            ItemsSource = new[] { "自动（中文↔英文）", "简体中文", "English" },
            Style = LanguageComboBoxStyle
        };
        language.SelectionChanged += async (_, _) => { if (IsLoaded && _request is not null) await TranslateAsync(); };
        toolbar.Children.Add(language);

        var originalToggle = MakeTextButton(_image is null ? "显示原文" : "显示原图", out var originalLabel);
        originalToggle.ToolTip = _image is null ? "显示或隐藏原文" : "显示或隐藏所选图片";
        Grid.SetColumn(originalToggle, 2);
        toolbar.Children.Add(originalToggle);
        Grid.SetRow(toolbar, 0);
        body.Children.Add(toolbar);

        var sourceGrid = new Grid();
        sourceText = new TextBlock
        {
            Text = _sourceText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = MutedBrush,
            Visibility = _image is null ? Visibility.Visible : Visibility.Collapsed
        };
        sourceImage = new Image
        {
            Source = _image is null ? null : ImageUtilities.ToPreview(_image),
            Stretch = Stretch.Uniform,
            MaxHeight = 118,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = _image is null ? Visibility.Collapsed : Visibility.Visible
        };
        sourceGrid.Children.Add(sourceText);
        sourceGrid.Children.Add(sourceImage);
        var sourcePanelLocal = new Border
        {
            Background = SourceBrush,
            BorderBrush = LightBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = _settings.ShowOriginalByDefault ? Visibility.Visible : Visibility.Collapsed,
            MaxHeight = 132,
            Child = sourceGrid
        };
        sourcePanel = sourcePanelLocal;
        UpdateSourceVisibility(sourcePanelLocal, originalToggle, originalLabel, _settings.ShowOriginalByDefault, persist: false);
        originalToggle.Click += (_, _) => UpdateSourceVisibility(
            sourcePanelLocal,
            originalToggle,
            originalLabel,
            sourcePanelLocal.Visibility != Visibility.Visible,
            persist: true);
        Grid.SetRow(sourcePanelLocal, 1);
        body.Children.Add(sourcePanelLocal);

        var outputScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0)
        };
        result = new TextBlock
        {
            Text = "正在连接模型并准备译文…",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            LineHeight = 25,
            Foreground = InkBrush
        };
        outputScroll.Content = result;
        var outputCard = new Border
        {
            Background = OutputBrush,
            BorderBrush = LightBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Child = outputScroll
        };
        Grid.SetRow(outputCard, 2);
        body.Children.Add(outputCard);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Border
        {
            Background = Brushes.White,
            BorderBrush = LightBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 13, 13),
            Padding = new Thickness(14, 0, 12, 0)
        };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = AccentBrush, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        status = new TextBlock
        {
            Text = "正在连接模型",
            FontSize = 10,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        statusRow.Children.Add(status);
        Grid.SetColumn(statusRow, 0);
        footerGrid.Children.Add(statusRow);

        var footerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        footerActions.Children.Add(MakeFooterButton("Esc  停止", StopOrClose, false, out _stopButton));
        footerActions.Children.Add(MakeFooterButton("↻  重译", async () => await TranslateAsync(), false, out _));
        footerActions.Children.Add(MakeFooterButton("复制译文", CopyResult, true, out _));
        Grid.SetColumn(footerActions, 1);
        footerGrid.Children.Add(footerActions);
        footer.Child = footerGrid;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        shell.Child = root;
        return shell;
    }

    private void UpdateSourceVisibility(Border panel, Button toggle, TextBlock label, bool showSource, bool persist)
    {
        panel.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;
        _settings.ShowOriginalByDefault = showSource;
        toggle.Tag = showSource;
        label.Text = showSource ? (_image is null ? "隐藏原文" : "隐藏原图") : (_image is null ? "显示原文" : "显示原图");
        toggle.Foreground = showSource ? AccentBrush : InkBrush;
        toggle.Background = showSource ? SelectedControlBrush : ControlBrush;
        toggle.BorderBrush = showSource ? SelectedBorderBrush : LightBorderBrush;

        if (!persist) return;
        try { SettingsStore.Save(_settings); }
        catch { _status.Text = "显示原文偏好保存失败"; }
    }

    private void DragMoveHeader()
    {
        _dragSurface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1 && e.OriginalSource is not Button) DragMove();
        };
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void QueueGeometrySave()
    {
        if (WindowState != WindowState.Normal || !IsVisible) return;
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Start();
    }

    private void CaptureWindowGeometry()
    {
        if (WindowState != WindowState.Normal) return;
        _settings.ResultWidth = Width;
        _settings.ResultHeight = Height;
        _settings.ResultLeft = Left;
        _settings.ResultTop = Top;
    }

    private async Task TranslateAsync()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var elapsed = Stopwatch.StartNew();
        var receivedFirstPiece = false;
        _renderTimer.Stop();
        _streamedText.Clear();
        _pendingRender = false;
        _completedText = string.Empty;
        _isTranslating = true;
        _stopButton.Content = "Esc  停止";
        _result.Text = "等待模型返回译文…";
        _status.Text = "正在连接模型";

        try
        {
            Action<string> append = piece =>
            {
                void AddPiece()
                {
                    if (!receivedFirstPiece)
                    {
                        receivedFirstPiece = true;
                        _status.Text = $"首段译文 · {elapsed.Elapsed.TotalSeconds:0.0} 秒";
                    }
                    else
                    {
                        _status.Text = "正在接收译文";
                    }
                    _streamedText.Append(piece);
                    _pendingRender = true;
                    if (!_renderTimer.IsEnabled) _renderTimer.Start();
                }

                if (Dispatcher.CheckAccess()) AddPiece();
                else Dispatcher.BeginInvoke(AddPiece);
            };
            Action connected = () => _status.Text = "已连通，模型生成中…";
            var target = _language.SelectedItem?.ToString() ?? "自动（中文↔英文）";
            if (_image is null)
            {
                target = ResolveTarget(_sourceText, target);
                await _client.TranslateTextAsync(_settings.ApiUrl, _apiKey, _settings.TextModel, target, _sourceText, append, _request.Token, connected, _settings.EnableThinking);
            }
            else
            {
                if (target.StartsWith("自动", StringComparison.Ordinal)) target = "the opposite language between Simplified Chinese and English, based on the language in the image";
                else if (target == "简体中文") target = "Simplified Chinese";
                await _client.TranslateImageAsync(_settings.ApiUrl, _apiKey, _settings.ImageModel, target, _image, append, _request.Token, connected, _settings.EnableThinking);
            }

            FlushStream();
            _completedText = _streamedText.ToString();
            if (_completedText.Length == 0) _result.Text = "模型没有返回译文，请检查所选内容或重试。";
            _status.Text = $"完成 · {elapsed.Elapsed.TotalSeconds:0.0} 秒";
        }
        catch (OperationCanceledException)
        {
            FlushStream();
            _completedText = _streamedText.ToString();
            _status.Text = "已停止";
            if (_completedText.Length == 0) _result.Text = "已停止。";
        }
        catch (Exception ex)
        {
            FlushStream();
            _completedText = _streamedText.ToString();
            _status.Text = _completedText.Length > 0 ? "连接中断 · 已保留已收到内容" : "翻译失败";
            if (_completedText.Length == 0) _result.Text = FriendlyError(ex);
        }
        finally
        {
            _isTranslating = false;
            _stopButton.Content = "Esc  关闭";
        }
    }

    private void FlushStream()
    {
        _renderTimer.Stop();
        if (!_pendingRender) return;
        _result.Text = _streamedText.ToString();
        _pendingRender = false;
    }

    private static string ResolveTarget(string text, string selected)
    {
        if (selected == "简体中文") return "Simplified Chinese";
        if (selected == "English") return "English";
        var cjk = text.Count(c => c is >= '\u4e00' and <= '\u9fff');
        var latin = text.Count(char.IsLetter);
        return cjk > latin * 0.12 ? "English" : "Simplified Chinese";
    }

    private static string FriendlyError(Exception ex)
    {
        if (ex is HttpRequestException http)
        {
            var status = (int?)http.StatusCode;
            if (status is 401 or 403) return "API Key 无效或没有调用权限。\n\n" + http.Message;
            if (status == 404) return "接口地址或模型 ID 不正确。\n\n" + http.Message;
            if (status == 429) return "请求频率或账户额度受限，请稍后重试。\n\n" + http.Message;
            return http.Message;
        }
        if (ex is TaskCanceledException) return "请求超时，请检查网络后重试。";
        return ex.Message;
    }

    private void CopyResult()
    {
        FlushStream();
        if (_streamedText.Length > 0) _completedText = _streamedText.ToString();
        if (string.IsNullOrWhiteSpace(_completedText)) return;
        if (ClipboardService.TrySetText(_completedText, out var error))
            _status.Text = "译文已复制到剪贴板";
        else
            _status.Text = error;
    }

    private void ResultKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { if (_isTranslating) _request?.Cancel(); else Close(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.R && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None) _ = TranslateAsync();
        else if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None) CopyResult();
    }

    private void StopOrClose()
    {
        if (_isTranslating) _request?.Cancel();
        else Close();
    }

    private static Button MakeHeaderButton(string label, string tooltip)
    {
        var button = new Button
        {
            Content = label,
            Width = 27,
            Height = 27,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = MutedBrush,
            FontSize = 15,
            ToolTip = tooltip,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedButtonTemplate
        };
        button.MouseEnter += (_, _) => button.Background = ControlHoverBrush;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        return button;
    }

    private static Button MakeTextButton(string text, out TextBlock label)
    {
        label = new TextBlock { Text = text, FontSize = 11, Foreground = InkBrush, VerticalAlignment = VerticalAlignment.Center };
        var button = new Button
        {
            Content = label,
            MinWidth = 106,
            Height = 36,
            Padding = new Thickness(10, 0, 10, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = LightBorderBrush,
            Background = ControlBrush,
            Foreground = InkBrush,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedButtonTemplate,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.MouseEnter += (_, _) => button.Background = ControlHoverBrush;
        button.MouseLeave += (_, _) =>
        {
            var selected = button.Tag is true;
            button.Background = selected ? SelectedControlBrush : ControlBrush;
            button.BorderBrush = selected ? SelectedBorderBrush : LightBorderBrush;
        };
        return button;
    }

    private static Style CreateLanguageComboBoxStyle() => (Style)XamlReader.Parse(
        """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="{x:Type ComboBox}">
          <Setter Property="SnapsToDevicePixels" Value="True" />
          <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled" />
          <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto" />
          <Setter Property="ItemContainerStyle">
            <Setter.Value>
              <Style TargetType="{x:Type ComboBoxItem}">
                <Setter Property="MinHeight" Value="34" />
                <Setter Property="Padding" Value="10,0" />
                <Setter Property="Foreground" Value="#FF3A3844" />
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="{x:Type ComboBoxItem}">
                      <Border x:Name="ItemFrame" Background="Transparent" CornerRadius="7" Padding="{TemplateBinding Padding}">
                        <ContentPresenter VerticalAlignment="Center" />
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="ItemFrame" Property="Background" Value="#FFF3F1FB" /></Trigger>
                        <Trigger Property="IsSelected" Value="True"><Setter TargetName="ItemFrame" Property="Background" Value="#FFF0EEFC" /><Setter Property="Foreground" Value="#FF6254CF" /></Trigger>
                        <Trigger Property="IsEnabled" Value="False"><Setter TargetName="ItemFrame" Property="Opacity" Value="0.5" /></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </Setter.Value>
          </Setter>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="{x:Type ComboBox}">
                <Grid>
                  <ToggleButton x:Name="ToggleButton"
                                Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                                Focusable="False" ClickMode="Press">
                    <ToggleButton.Template>
                      <ControlTemplate TargetType="{x:Type ToggleButton}">
                        <Border x:Name="ToggleFrame" Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" />
                        <ControlTemplate.Triggers>
                          <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ToggleFrame" Property="Background" Value="#FFFAF9FD" /></Trigger>
                          <Trigger Property="IsPressed" Value="True"><Setter TargetName="ToggleFrame" Property="Background" Value="#FFF0EEFA" /></Trigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </ToggleButton.Template>
                  </ToggleButton>
                  <ContentPresenter x:Name="ContentSite" IsHitTestVisible="False"
                                    Content="{TemplateBinding SelectionBoxItem}"
                                    ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                    ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"
                                    TextElement.Foreground="{TemplateBinding Foreground}"
                                    Margin="12,0,34,0" VerticalAlignment="Center" HorizontalAlignment="Left" />
                  <Path x:Name="Arrow" Data="M 0,0 L 4,4 L 8,0" Stroke="#FF817C90" StrokeThickness="1.6"
                        StrokeStartLineCap="Round" StrokeEndLineCap="Round" Fill="Transparent"
                        Width="8" Height="5" HorizontalAlignment="Right" VerticalAlignment="Center"
                        Margin="0,0,14,0" IsHitTestVisible="False" RenderTransformOrigin="0.5,0.5" />
                  <Popup x:Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}"
                         AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                    <Border Background="White" BorderBrush="#FFE4E1EC" BorderThickness="1" CornerRadius="10" Padding="4">
                      <Border.Effect><DropShadowEffect Color="#302A204F" BlurRadius="14" ShadowDepth="3" Opacity="0.16" /></Border.Effect>
                      <ScrollViewer MaxHeight="220" CanContentScroll="True" VerticalScrollBarVisibility="{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}">
                        <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained" />
                      </ScrollViewer>
                    </Border>
                  </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsDropDownOpen" Value="True">
                    <Setter TargetName="Arrow" Property="RenderTransform"><Setter.Value><RotateTransform Angle="180" /></Setter.Value></Setter>
                    <Setter TargetName="ToggleButton" Property="BorderBrush" Value="#FFC8C0F1" />
                  </Trigger>
                  <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="ToggleButton" Property="BorderBrush" Value="#FFA99CF0" /></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter TargetName="ToggleButton" Property="Opacity" Value="0.55" /></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);

    private static Button MakeFooterButton(string label, Action action, bool primary, out Button button)
    {
        var createdButton = new Button
        {
            Content = label,
            Padding = new Thickness(primary ? 13 : 10, 6, primary ? 13 : 10, 6),
            Margin = new Thickness(3, 0, 0, 0),
            FontSize = 10,
            FontWeight = primary ? FontWeights.Medium : FontWeights.Normal,
            BorderThickness = new Thickness(0),
            Background = primary ? AccentBrush : ControlBrush,
            Foreground = primary ? Brushes.White : InkBrush,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedButtonTemplate
        };
        button = createdButton;
        createdButton.MouseEnter += (_, _) => createdButton.Opacity = 0.86;
        createdButton.MouseLeave += (_, _) => createdButton.Opacity = 1;
        createdButton.Click += (_, _) => action();
        return createdButton;
    }

    private static Button MakeFooterButton(string label, Func<Task> action, bool primary, out Button button)
    {
        var createdButton = new Button
        {
            Content = label,
            Padding = new Thickness(primary ? 13 : 10, 6, primary ? 13 : 10, 6),
            Margin = new Thickness(3, 0, 0, 0),
            FontSize = 10,
            FontWeight = primary ? FontWeights.Medium : FontWeights.Normal,
            BorderThickness = new Thickness(0),
            Background = primary ? AccentBrush : ControlBrush,
            Foreground = primary ? Brushes.White : InkBrush,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = RoundedButtonTemplate
        };
        button = createdButton;
        createdButton.MouseEnter += (_, _) => createdButton.Opacity = 0.86;
        createdButton.MouseLeave += (_, _) => createdButton.Opacity = 1;
        createdButton.Click += async (_, _) => await action();
        return createdButton;
    }

    private static ControlTemplate CreateRoundedButtonTemplate() => (ControlTemplate)XamlReader.Parse(
        """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="{x:Type Button}">
          <Border CornerRadius="8" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              Margin="{TemplateBinding Padding}"
                              RecognizesAccessKey="True" />
          </Border>
        </ControlTemplate>
        """);

    private static readonly ControlTemplate RoundedButtonTemplate = CreateRoundedButtonTemplate();
    private static readonly Style LanguageComboBoxStyle = CreateLanguageComboBoxStyle();
    private static readonly System.Windows.Media.Brush InkBrush = new SolidColorBrush(Color.FromRgb(45, 43, 54));
    private static readonly System.Windows.Media.Brush MutedBrush = new SolidColorBrush(Color.FromRgb(117, 114, 128));
    private static readonly System.Windows.Media.Brush AccentBrush = new SolidColorBrush(Color.FromRgb(116, 100, 232));
    private static readonly System.Windows.Media.Brush LogoBrush = new SolidColorBrush(Color.FromRgb(241, 239, 255));
    private static readonly System.Windows.Media.Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(250, 249, 253));
    private static readonly System.Windows.Media.Brush ControlBrush = new SolidColorBrush(Color.FromRgb(246, 245, 250));
    private static readonly System.Windows.Media.Brush ControlHoverBrush = new SolidColorBrush(Color.FromRgb(238, 236, 247));
    private static readonly System.Windows.Media.Brush SelectedControlBrush = new SolidColorBrush(Color.FromRgb(241, 239, 255));
    private static readonly System.Windows.Media.Brush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(213, 207, 244));
    private static readonly System.Windows.Media.Brush SourceBrush = new SolidColorBrush(Color.FromRgb(248, 247, 251));
    private static readonly System.Windows.Media.Brush OutputBrush = new SolidColorBrush(Color.FromRgb(253, 252, 255));
    private static readonly System.Windows.Media.Brush LightBorderBrush = new SolidColorBrush(Color.FromRgb(235, 232, 243));
}
