using System.IO;

namespace ClipStack.Core;

public enum ClipKind { Text = 0, Image = 1, Files = 2 }

/// <summary>Clipboard content as captured, before it becomes a history item.</summary>
public sealed class CapturedClip
{
    public ClipKind Kind { get; init; }
    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public string[]? Files { get; init; }

    /// <summary>PNG-encoded image (full size).</summary>
    public byte[]? ImagePng { get; init; }
    /// <summary>PNG-encoded thumbnail.</summary>
    public byte[]? ThumbPng { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    /// <summary>Raw bytes identifying the image content (pixels), used only for duplicate hashing.</summary>
    public byte[]? ImageIdentity { get; init; }

    public string? SourceApp { get; init; }
    public string? SourcePath { get; init; }
}

/// <summary>One entry of the clipboard history, as held in memory.</summary>
public sealed class ClipItem
{
    public long Id { get; set; }
    public ClipKind Kind { get; init; }

    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public string[]? Files { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }

    public DateTime CreatedAt { get; init; }
    /// <summary>Last time this content was copied or pasted; drives "recent" ordering.</summary>
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }
    public int CopyCount { get; set; } = 1;
    public bool IsPinned { get; set; }
    public DateTime? PinnedAt { get; set; }

    public string? SourceApp { get; init; }
    public string? SourcePath { get; init; }
    public string Hash { get; init; } = "";

    /// <summary>Thumbnail PNG, loaded lazily for image items.</summary>
    public byte[]? ThumbPng { get; set; }

    public bool HasRichText => !string.IsNullOrEmpty(Html) || !string.IsNullOrEmpty(Rtf);

    private string? _searchText;
    /// <summary>Lower-cased text used for matching (capped for speed).</summary>
    public string SearchText => _searchText ??= BuildSearchText();

    private const int MaxSearchChars = 20_000;

    private string BuildSearchText()
    {
        string s = Kind switch
        {
            ClipKind.Text => Text ?? "",
            ClipKind.Files => string.Join("\n", (Files ?? []).Select(f => f + "\n" + Path.GetFileName(f.TrimEnd('\\')))),
            ClipKind.Image => $"image {ImageWidth}x{ImageHeight}",
            _ => "",
        };
        if (s.Length > MaxSearchChars) s = s[..MaxSearchChars];
        return s.ToLowerInvariant();
    }

    /// <summary>The text to use for plain-text paste and "copy" of non-text items.</summary>
    public string PlainText => Kind switch
    {
        ClipKind.Text => Text ?? "",
        ClipKind.Files => string.Join(Environment.NewLine, Files ?? []),
        _ => "",
    };

    public bool IsUrl => Kind == ClipKind.Text && TryGetUrl(out _);

    public bool TryGetUrl(out Uri? uri)
    {
        uri = null;
        var t = Text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 4096 || t.Contains(' ') || t.Contains('\n')) return false;
        return Uri.TryCreate(t, UriKind.Absolute, out uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "ftp");
    }
}
