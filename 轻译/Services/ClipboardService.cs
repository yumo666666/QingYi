using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace QingYi.Services;

public static class ClipboardService
{
    public static bool TrySetText(string text, out string error)
    {
        var data = new Forms.DataObject();
        data.SetData(Forms.DataFormats.UnicodeText, text);
        data.SetData(Forms.DataFormats.Text, text);
        return TrySetData(data, out error);
    }

    public static bool TrySetImage(byte[] png, out string error)
    {
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            using var bitmap = new Bitmap(stream);
            var data = new Forms.DataObject();
            data.SetData(Forms.DataFormats.Bitmap, false, bitmap);
            return TrySetData(data, out error);
        }
        catch (Exception ex)
        {
            error = Explain(ex);
            return false;
        }
    }

    private static bool TrySetData(Forms.DataObject data, out string error)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            error = "复制失败：剪贴板操作需要在 Windows 主线程执行。";
            return false;
        }

        try
        {
            // The WinForms clipboard overload retries when another process briefly owns the clipboard.
            Forms.Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 100);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = Explain(ex);
            return false;
        }
    }

    private static string Explain(Exception ex)
    {
        if (ex is ThreadStateException)
            return "复制失败：剪贴板操作需要在 Windows 主线程执行。";

        if (ex is ExternalException && unchecked((uint)ex.HResult) == 0x800401D0)
            return "剪贴板正被其他程序占用，已重试约 1 秒仍未成功。请稍后再试。";

        return $"复制失败：{ex.Message}（0x{unchecked((uint)ex.HResult):X8}）";
    }
}
