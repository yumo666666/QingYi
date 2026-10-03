using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;

namespace QingYi.Windows;

public sealed class ToolbarVisual : Border
{
    private readonly Border _actionFrame;
    private readonly Border _copyFrame;
    private readonly TextBlock _copyGlyph;
    private readonly TextBlock _copyLabel;

    public Border Grip { get; }
    public Button TranslateButton { get; }
    public Button CopyButton { get; }

    public ToolbarVisual(bool imageMode)
    {
        Background = Brushes.White;
        CornerRadius = new CornerRadius(13);
        BorderBrush = new SolidColorBrush(Color.FromRgb(231, 228, 244));
        BorderThickness = new Thickness(1);
        Effect = new DropShadowEffect { Color = Color.FromRgb(58, 49, 110), BlurRadius = 18, ShadowDepth = 4, Opacity = 0.18 };
        Padding = new Thickness(8, 6, 8, 6);
        Margin = new Thickness(4, 3, 4, 7);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = 38, VerticalAlignment = VerticalAlignment.Center };
        Grip = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromRgb(241, 239, 255)),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            ToolTip = "按住此处拖动工具栏",
            Child = new TextBlock
            {
                Text = "译",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = AccentBrush,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        row.Children.Add(Grip);

        var actionContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionContent.Children.Add(new TextBlock
        {
            Text = imageMode ? "▧" : "文A",
            FontSize = imageMode ? 15 : 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        actionContent.Children.Add(new TextBlock
        {
            Text = imageMode ? "图片翻译" : "翻译",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        _actionFrame = new Border
        {
            Background = AccentBrush,
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(8, 0, 0, 0)
        };
        TranslateButton = new Button
        {
            Content = actionContent,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 12, 0),
            Height = 32,
            MinWidth = imageMode ? 104 : 88,
            Template = ToolbarButtonTemplate,
            FocusVisualStyle = null,
            OverridesDefaultStyle = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = imageMode ? "翻译框选区域中的文字" : "翻译所选文字"
        };
        _actionFrame.Child = TranslateButton;
        TranslateButton.MouseEnter += (_, _) => _actionFrame.Background = AccentHoverBrush;
        TranslateButton.MouseLeave += (_, _) => _actionFrame.Background = AccentBrush;
        row.Children.Add(_actionFrame);

        row.Children.Add(new Border
        {
            Width = 1,
            Height = 21,
            Background = new SolidColorBrush(Color.FromRgb(232, 230, 239)),
            Margin = new Thickness(9, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        _copyGlyph = new TextBlock { Text = "▢", FontSize = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        _copyLabel = new TextBlock { Text = "复制", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var copyContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        copyContent.Children.Add(_copyGlyph);
        copyContent.Children.Add(_copyLabel);
        _copyFrame = new Border
        {
            MinWidth = 82,
            Height = 32,
            Background = CopyIdleBrush,
            CornerRadius = new CornerRadius(9)
        };
        CopyButton = new Button
        {
            Content = copyContent,
            Foreground = InkBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Template = ToolbarButtonTemplate,
            FocusVisualStyle = null,
            OverridesDefaultStyle = true,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = imageMode ? "复制框选图片" : "复制所选文字"
        };
        _copyFrame.Child = CopyButton;
        CopyButton.MouseEnter += (_, _) => { if (!_copyFeedbackVisible) _copyFrame.Background = CopyHoverBrush; };
        CopyButton.MouseLeave += (_, _) => { if (!_copyFeedbackVisible) _copyFrame.Background = CopyIdleBrush; };
        row.Children.Add(_copyFrame);

        Child = row;
    }

    private bool _copyFeedbackVisible;

    public void ShowCopyFeedback(bool success, string? error = null)
    {
        _copyFeedbackVisible = true;
        // Keep success feedback text-only; the check glyph looked like a stray slash at this size.
        _copyGlyph.Text = success ? string.Empty : "×";
        _copyGlyph.Margin = success ? new Thickness(0) : new Thickness(0, 0, 5, 0);
        _copyLabel.Text = success ? "已复制" : "失败";
        _copyFrame.Background = success ? CopySuccessBrush : CopyFailureBrush;
        CopyButton.ToolTip = success ? "已复制到剪贴板" : string.IsNullOrWhiteSpace(error) ? "复制失败，请重试" : error;
    }

    public void ResetCopyFeedback()
    {
        _copyFeedbackVisible = false;
        _copyGlyph.Text = "▢";
        _copyGlyph.Margin = new Thickness(0, 0, 5, 0);
        _copyLabel.Text = "复制";
        _copyFrame.Background = CopyIdleBrush;
    }

    public static UIElement CreatePreview(double scale) => new ToolbarVisual(false)
    {
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        LayoutTransform = new ScaleTransform(Math.Clamp(scale, 0.75, 1.4), Math.Clamp(scale, 0.75, 1.4))
    };

    private static readonly ControlTemplate ToolbarButtonTemplate = (ControlTemplate)XamlReader.Parse(
        """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         TargetType="{x:Type Button}">
          <Border Background="{TemplateBinding Background}" CornerRadius="9" SnapsToDevicePixels="True">
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True" />
          </Border>
        </ControlTemplate>
        """);

    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(116, 100, 232));
    private static readonly Brush AccentHoverBrush = new SolidColorBrush(Color.FromRgb(106, 90, 218));
    private static readonly Brush InkBrush = new SolidColorBrush(Color.FromRgb(58, 56, 68));
    private static readonly Brush CopyIdleBrush = new SolidColorBrush(Color.FromRgb(246, 245, 250));
    private static readonly Brush CopyHoverBrush = new SolidColorBrush(Color.FromRgb(239, 237, 248));
    private static readonly Brush CopySuccessBrush = new SolidColorBrush(Color.FromRgb(230, 247, 238));
    private static readonly Brush CopyFailureBrush = new SolidColorBrush(Color.FromRgb(255, 237, 237));
}
