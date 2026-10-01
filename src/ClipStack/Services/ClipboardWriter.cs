using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipStack.Core;

namespace ClipStack.Services;

/// <summary>Puts a history item back on the Windows clipboard, with all the formats it was captured with.</summary>
public sealed class ClipboardWriter
{
    private readonly HistoryService _history;
    private readonly ClipboardMonitor _monitor;

    public ClipboardWriter(HistoryService history, ClipboardMonitor monitor)
    {
        _history = history;
        _monitor = monitor;
    }

    public bool Write(ClipItem item, bool plainText = false)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Text:
                data.SetData(DataFormats.UnicodeText, item.Text ?? "");
                if (!plainText)
                {
                    if (item.Html is not null) data.SetData(DataFormats.Html, item.Html);
                    if (item.Rtf is not null) data.SetData(DataFormats.Rtf, item.Rtf);
                }
                break;

            case ClipKind.Files:
                if (plainText)
                {
                    data.SetData(DataFormats.UnicodeText, item.PlainText);
                    break;
                }
                var list = new StringCollection();
                list.AddRange(item.Files ?? []);
                data.SetFileDropList(list);
                // DROPEFFECT_COPY: pasting copies the files rather than moving them.
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                data.SetData(DataFormats.UnicodeText, item.PlainText);
                break;

            case ClipKind.Image:
                var png = _history.LoadImage(item);
                if (png is null) return false;
                var frame = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                data.SetImage(frame);
                // Many apps (browsers, Office) prefer PNG because it keeps transparency.
                data.SetData("PNG", new MemoryStream(png));
                break;
        }

        try
        {
            ClipboardMonitor.Retry(() => { Clipboard.SetDataObject(data, copy: true); return true; });
            _monitor.IgnoreCurrentContent();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not write to the clipboard", ex);
            return false;
        }
    }
}
