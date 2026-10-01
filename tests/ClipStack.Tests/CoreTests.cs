using System.IO;
using System.Windows.Input;
using ClipStack.Core;
using ClipStack.Services;
using ClipStack.UI;

namespace ClipStack.Tests;

public class SearchEngineTests
{
    private static ClipItem Text(string text, int minutesAgo = 0, bool pinned = false, int uses = 0) => new()
    {
        Kind = ClipKind.Text, Text = text, CreatedAt = DateTime.Now.AddMinutes(-minutesAgo),
        UpdatedAt = DateTime.Now.AddMinutes(-minutesAgo), IsPinned = pinned, UseCount = uses, Hash = text,
    };

    [Fact]
    public void EmptyQueryKeepsOrder()
    {
        var items = new List<ClipItem> { Text("a"), Text("b") };
        Assert.Equal(items, SearchEngine.Search(items, "  "));
    }

    [Fact]
    public void RanksExactThenPrefixThenSubstringThenFuzzy()
    {
        var fuzzy = Text("go into the hub", 0);
        var substring = Text("my github repo", 1);
        var prefix = Text("github.com/user/project", 2);
        var exact = Text("GitHub", 3);
        var none = Text("unrelated", 4);
        var result = SearchEngine.Search([fuzzy, substring, prefix, exact, none], "github");
        Assert.Equal([exact, prefix, substring, fuzzy], result);
    }

    [Fact]
    public void CaseInsensitiveAndMultiline()
    {
        var item = Text("first line\nSecond LINE with Token");
        Assert.Single(SearchEngine.Search([item], "with token"));
    }

    [Fact]
    public void AllWordsMatchInAnyOrder()
    {
        var item = Text("meeting notes for tomorrow");
        Assert.Single(SearchEngine.Search([item], "tomorrow meeting"));
    }

    [Fact]
    public void RecencyBreaksTies()
    {
        var older = Text("hello world", 10);
        var newer = Text("hello there", 1);
        Assert.Equal([newer, older], SearchEngine.Search([older, newer], "hello"));
    }

    [Fact]
    public void UsageBoostsRanking()
    {
        var frequent = Text("deploy script v1", 10, uses: 10);
        var recent = Text("deploy notes", 1);
        Assert.Equal(frequent, SearchEngine.Search([recent, frequent], "deploy")[0]);
        Assert.Equal(recent, SearchEngine.Search([recent, frequent], "deploy", useUsage: false)[0]);
    }

    [Fact]
    public void ScatteredFuzzyMatchesAreRejected()
    {
        var item = Text("a long sentence where every letter of the query eventually appears somewhere far apart");
        Assert.Empty(SearchEngine.Search([item], "zqx"));
    }

    [Fact]
    public void FuzzyMatchesMustStartAtAWord()
    {
        Assert.Empty(SearchEngine.Search([Text("Tomorrow's meeting is at 10:00")], "git"));
        Assert.Single(SearchEngine.Search([Text("calculate_total(items)")], "caltot"));
    }

    [Fact]
    public void FilesAreSearchableByName()
    {
        var item = new ClipItem { Kind = ClipKind.Files, Files = [@"C:\Users\Me\Documents\report.pdf"], Hash = "f" };
        Assert.Single(SearchEngine.Search([item], "report"));
    }

    [Fact]
    public void ThousandsOfItemsSearchQuickly()
    {
        var items = Enumerable.Range(0, 20_000).Select(i => Text($"item number {i} with some text about things {i * 7}", i)).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = SearchEngine.Search(items, "things 7");
        sw.Stop();
        Assert.NotEmpty(result);
        Assert.True(sw.ElapsedMilliseconds < 500, $"search took {sw.ElapsedMilliseconds} ms");
    }
}

public class SensitiveDetectorTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEow...\n-----END RSA PRIVATE KEY-----")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nabc")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("ghp_1234567890abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("export OPENAI_API_KEY=sk-proj-abcdefghijklmnopqrstuvwx")]
    [InlineData("xoxb-123456789012-abcdefghij")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("5500-0000-0000-0004")]
    [InlineData("123456")]
    [InlineData("Tr0ub4dor&3xK")]
    [InlineData("q7#Vz!9pLm$2")]
    [InlineData("aZ3kP9qL2mX8vB7nC4tR6yW1sD5fG0hJ")]
    public void DetectsSecrets(string text) => Assert.NotNull(SensitiveDetector.Detect(text));

    [Theory]
    [InlineData("Hello world")]
    [InlineData("https://github.com/user/project?tab=readme&x=1")]
    [InlineData("C:\\Users\\Me\\Documents\\report.pdf")]
    [InlineData("def hello():\n    print(\"Hello\")")]
    [InlineData("HttpClient")]
    [InlineData("my_variable_name1")]
    [InlineData("john.doe@example.com")]
    [InlineData("4111 1111 1111 1112")] // fails Luhn
    [InlineData("12345")]
    [InlineData("2024-01-15")]
    [InlineData("e83c5163316f89bfbde7d9ab23ca2e25604af290")] // git SHA
    [InlineData("Meeting at 10:00 tomorrow!")]
    [InlineData("System.Collections.Generic.List`1")]
    [InlineData("")]
    public void IgnoresOrdinaryText(string text) => Assert.Null(SensitiveDetector.Detect(text));
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+V", ModifierKeys.Control | ModifierKeys.Shift, Key.V)]
    [InlineData("ctrl + alt + v", ModifierKeys.Control | ModifierKeys.Alt, Key.V)]
    [InlineData("Win+V", ModifierKeys.Windows, Key.V)]
    [InlineData("Ctrl+Alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1)]
    [InlineData("Ctrl+F12", ModifierKeys.Control, Key.F12)]
    public void ParsesAndRoundTrips(string text, ModifierKeys mods, Key key)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, key), hk);
        Assert.True(Hotkey.TryParse(hk.ToSetting(), out var again));
        Assert.Equal(hk, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Banana")]
    public void RejectsInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));
}

public class PreviewTests
{
    [Fact]
    public void TextTitleIsFirstNonBlankLineCollapsed()
    {
        var item = new ClipItem { Kind = ClipKind.Text, Text = "\n\n   def   calculate_total(items):\n    return 1" };
        Assert.Equal("def calculate_total(items):", RowViewModel.BuildTitle(item, 120));
        Assert.StartsWith("2 lines", RowViewModel.BuildSubtitle(item, showTime: false));
    }

    [Fact]
    public void LongTextIsTruncated()
    {
        var item = new ClipItem { Kind = ClipKind.Text, Text = new string('x', 500) };
        var title = RowViewModel.BuildTitle(item, 50);
        Assert.Equal(50, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void UrlShowsHostAndPath()
    {
        var item = new ClipItem { Kind = ClipKind.Text, Text = "https://github.com/user/project/" };
        Assert.Equal("github.com/user/project", RowViewModel.BuildTitle(item, 120));
    }

    [Fact]
    public void FilesShowNameOrCount()
    {
        Assert.Equal("report.pdf", RowViewModel.BuildTitle(new ClipItem { Kind = ClipKind.Files, Files = [@"C:\a\report.pdf"] }, 120));
        Assert.Equal("4 files", RowViewModel.BuildTitle(new ClipItem { Kind = ClipKind.Files, Files = ["a", "b", "c", "d"] }, 120));
    }
}

public class ExclusionTests
{
    [Theory]
    [InlineData(@"C:\Program Files\KeePass\KeePass.exe", true)]
    [InlineData(@"C:\Apps\keepass.EXE", true)]
    [InlineData(@"C:\Apps\Bitwarden.exe", true)]
    [InlineData(@"C:\Apps\chrome.exe", false)]
    [InlineData(null, false)]
    public void MatchesByExecutableName(string? path, bool excluded)
    {
        var s = new AppSettings { ExcludedApps = ["KeePass.exe", "Bitwarden"] };
        Assert.Equal(excluded, ClipboardMonitor.IsExcludedApp(path, s));
    }
}

public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClipStackTests", Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();

    public HistoryTests() => Directory.CreateDirectory(_dir);

    private HistoryService Open() =>
        new(new HistoryStore(Path.Combine(_dir, "history.db")), new Protector(Path.Combine(_dir, "hash.key")), () => _settings);

    private static CapturedClip TextClip(string text) => new() { Kind = ClipKind.Text, Text = text, SourceApp = "Test" };

    private static ClipItem AddText(HistoryService h, string text) => h.Add(TextClip(text), h.ComputeHash(TextClip(text)));

    [Fact]
    public void DuplicatesMoveToTopInsteadOfRepeating()
    {
        using var h = Open();
        var first = AddText(h, "alpha");
        Thread.Sleep(5);
        AddText(h, "beta");
        Thread.Sleep(5);
        var again = AddText(h, "alpha");

        Assert.Same(first, again);
        Assert.Equal(2, h.Items.Count);
        Assert.Equal(2, again.CopyCount);
        Assert.Equal("alpha", h.Ordered()[0].Text);
    }

    [Fact]
    public void DuplicatesCanBeAllowed()
    {
        _settings.AllowDuplicates = true;
        using var h = Open();
        AddText(h, "alpha");
        AddText(h, "alpha");
        Assert.Equal(2, h.Items.Count);
    }

    [Fact]
    public void LimitRemovesOldestUnpinnedButNeverPinned()
    {
        _settings.MaxItems = 3;
        using var h = Open();
        var pinned = AddText(h, "pinned");
        h.TogglePin(pinned);
        for (int i = 0; i < 5; i++) { Thread.Sleep(2); AddText(h, $"item {i}"); }

        Assert.Equal(3, h.Items.Count);
        Assert.Contains(pinned, h.Items);
        Assert.Equal(["pinned", "item 4", "item 3"], h.Ordered().Select(i => i.Text));
    }

    [Fact]
    public void ClearKeepsPinnedUnlessAskedOtherwise()
    {
        using var h = Open();
        h.TogglePin(AddText(h, "keep"));
        AddText(h, "drop");
        h.Clear(includePinned: false);
        Assert.Equal(["keep"], h.Items.Select(i => i.Text));
        h.Clear(includePinned: true);
        Assert.Empty(h.Items);
    }

    [Fact]
    public void AgeCleanupRespectsPinnedSetting()
    {
        _settings.AutoCleanup = CleanupAge.OneHour;
        using var h = Open();
        var old = AddText(h, "old");
        var oldPinned = AddText(h, "old pinned");
        h.TogglePin(oldPinned);
        old.UpdatedAt = oldPinned.UpdatedAt = DateTime.Now.AddHours(-2);
        AddText(h, "fresh");

        h.EnforceLimits();
        Assert.Equal(["old pinned", "fresh"], h.Ordered().Select(i => i.Text));

        _settings.CleanupIncludesPinned = true;
        h.EnforceLimits();
        Assert.Equal(["fresh"], h.Ordered().Select(i => i.Text));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistsAcrossRestarts(bool encrypt)
    {
        _settings.EncryptHistory = encrypt;
        using (var h = Open())
        {
            h.Add(new CapturedClip { Kind = ClipKind.Text, Text = "secret-ish note", Html = "<b>note</b>", Rtf = @"{\rtf1 note}" },
                h.ComputeHash(TextClip("secret-ish note")));
            var pinned = AddText(h, "pin me");
            h.TogglePin(pinned);
            h.Add(new CapturedClip { Kind = ClipKind.Files, Files = [@"C:\x\a.txt", @"C:\x\b.txt"] }, "files-hash");
            h.MarkUsed(pinned);
        }

        using var reopened = Open();
        Assert.Equal(3, reopened.Items.Count);
        var note = reopened.Items.Single(i => i.Text == "secret-ish note");
        Assert.Equal("<b>note</b>", note.Html);
        Assert.Equal(@"{\rtf1 note}", note.Rtf);
        var pin = reopened.Items.Single(i => i.Text == "pin me");
        Assert.True(pin.IsPinned);
        Assert.Equal(1, pin.UseCount);
        Assert.Equal([@"C:\x\a.txt", @"C:\x\b.txt"], reopened.Items.Single(i => i.Kind == ClipKind.Files).Files!);

        // With encryption on, the plaintext must not appear anywhere in the database file.
        reopened.Flush();
        var raw = ReadShared(reopened.DatabasePath);
        var walPath = reopened.DatabasePath + "-wal";
        if (File.Exists(walPath)) raw = raw.Concat(ReadShared(walPath)).ToArray();
        bool containsPlaintext = System.Text.Encoding.UTF8.GetString(raw).Contains("secret-ish note");
        Assert.Equal(!encrypt, containsPlaintext);
    }

    [Fact]
    public async Task EncryptionCanBeToggledForExistingRows()
    {
        _settings.EncryptHistory = false;
        using (var h = Open())
        {
            AddText(h, "toggle me");
            h.Flush();
            Assert.Equal(1, await h.SetEncryptionAsync(true));
        }
        using var reopened = Open();
        Assert.Equal("toggle me", reopened.Items.Single().Text);
    }

    [Fact]
    public void DeleteSimilarMatchesSameSiteOrSameText()
    {
        using var h = Open();
        var a = AddText(h, "https://github.com/a");
        AddText(h, "https://github.com/b");
        AddText(h, "https://example.com");
        h.DeleteSimilar(a);
        Assert.Equal(["https://example.com"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void HashesAreKeyedAndStable()
    {
        using var h = Open();
        var h1 = h.ComputeHash(TextClip("abc"));
        Assert.Equal(h1, h.ComputeHash(TextClip("abc")));
        Assert.NotEqual(h1, h.ComputeHash(TextClip("abd")));
        // Keyed: differs from a plain SHA-256 of the content.
        var plain = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("abc")));
        Assert.NotEqual(plain, h1);
    }

    private static byte[] ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }
}
