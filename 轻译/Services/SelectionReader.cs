using System.Drawing;
using System.Windows.Automation;

namespace QingYi.Services;

public static class SelectionReader
{
    public static string? TryReadAt(Point point)
    {
        var offsets = new[] { new Point(0, 0), new Point(-5, 0), new Point(0, -7), new Point(-8, -7), new Point(8, 0) };
        foreach (var offset in offsets)
        {
            try
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(point.X + offset.X, point.Y + offset.Y));
                for (var depth = 0; element is not null && depth < 9; depth++, element = TreeWalker.ControlViewWalker.GetParent(element))
                {
                    if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject)) continue;
                    var pattern = (TextPattern)patternObject;
                    var selected = pattern.GetSelection();
                    if (selected.Length == 0) continue;
                    var value = string.Join("\n", selected.Select(range => range.GetText(12001))).Trim();
                    if (value.Length is > 0 and <= 12000) return value;
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (Exception) { }
        }
        return null;
    }
}
