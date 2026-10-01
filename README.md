# ClipStack

A small, fast, private clipboard history for Windows.

**Copy anything → press `Ctrl + Shift + V` → type to find it → `Enter` pastes it into the app you were using.**

Native C# / .NET 9 / WPF, about 2 MB, no Electron. It never connects to the internet.

## Run it

Requirements: Windows 10 1809+ or Windows 11, and the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
(or use the self-contained build below).

```powershell
# development
dotnet run --project src\ClipStack

# release build → dist\ClipStack.exe
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1                 # ~2 MB, needs .NET 9 runtime
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -SelfContained  # bundles the runtime
```

The first launch shows a short welcome window. After that, ClipStack lives in the system tray.
Running `ClipStack.exe` again while it's running opens Settings, which is useful if the tray icon is hidden.

## Using it

| Key | In the history window |
|---|---|
| *type* | search (instant, case-insensitive, fuzzy) |
| `↑` `↓` `PgUp` `PgDn` `Home` `End` | move the selection |
| `Enter` / click | paste into the previous app |
| `Shift + Enter` / Shift-click | paste as plain text |
| `Ctrl + Enter` | paste and keep the window open |
| `Ctrl + 1` … `Ctrl + 9` | paste the n-th item |
| `Ctrl + C` | copy to the clipboard without pasting |
| `Ctrl + P` | pin / unpin |
| `Delete` | delete the item. In the search box, use `Shift + Delete` unless the caret is at the end |
| `Ctrl + F` | focus / select the search text |
| `Esc` | close |
| right-click / `Menu` key | Paste, Copy, Paste as Plain Text, Pin, Delete, Delete All Similar, Show Details |

Supported content: text (plain, plus HTML/RTF formatting), images (shown as thumbnails), and files/folders copied in Explorer.
Pasting a file item pastes the original files. Nothing is duplicated on disk.

## Privacy

- History is stored locally in SQLite under `%LOCALAPPDATA%\ClipStack`. The folder is restricted to your Windows account.
- **Encrypted at rest** by default with Windows DPAPI, so only your account on this PC can decrypt it. Duplicate
  detection uses a keyed HMAC, so stored hashes can't be used to guess short secrets.
- **Sensitive content is not saved** (on by default). Detection runs locally and covers private keys, API tokens (AWS, GitHub,
  OpenAI/Anthropic, Slack, Stripe, Google, GitLab, npm), JWTs, Luhn-valid card numbers, 6/8-digit one-time codes, and
  password-like strings.
- Content that apps mark as private (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`,
  `CanIncludeInClipboardHistory = 0`) is never saved. Password managers such as KeePass and 1Password use these flags.
- **Excluded applications** (Settings → Privacy): nothing copied from them is saved. The main password managers are listed by default.
- **Pause monitoring** from the tray menu.
- The log contains events and errors only, never clipboard content.

## Project layout

```
src/ClipStack/
  Core/          platform-independent logic (unit tested)
    HistoryService.cs    in-memory history, de-duplication, limits, cleanup, serial DB write queue
    HistoryStore.cs      SQLite persistence, per-row DPAPI encryption
    SearchEngine.cs      ranking: exact > prefix > word > substring > all-words > fuzzy, usage + recency
    SensitiveDetector.cs local secret detection
    Protector.cs         DPAPI + keyed content hashing
    AppSettings.cs       settings.json
  Services/      Windows integration
    ClipboardMonitor.cs  AddClipboardFormatListener, debounced reads, source app detection, image processing
    ClipboardWriter.cs   restores all captured formats
    HotkeyManager.cs     RegisterHotKey, conflict detection
    PasteService.cs      refocus previous window + SendInput Ctrl+V
    ForegroundTracker.cs remembers the last real app (so opening from the tray can still paste)
    StartupManager.cs    HKCU Run key
  UI/            popup, settings, details, onboarding, tray
  AppController.cs       composition root
tests/ClipStack.Tests/   xUnit tests for Core
scripts/                 publish + end-to-end smoke test
```

## Testing

```powershell
dotnet test                                                         # unit tests
powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1      # real desktop workflow (~20 s, hands off the keyboard)
```

The E2E script uses a throwaway profile. It copies content, opens the popup over a test window with the real
hotkey, then searches, pastes, pins, deletes and restarts, checking each step.
`ClipStack.exe --render-preview <dir>` renders the windows with sample data to PNGs, for checking the UI quickly.

To develop without touching your real history, set `CLIPSTACK_DATA_DIR` to another folder before launching.

## Known limitations

- Windows blocks synthetic input into apps running **as administrator** (UIPI). For those, the item is copied
  to the clipboard; press `Ctrl + V` yourself.
- `Win + V` is reserved by Windows and can't be used as the shortcut. Settings tells you when a shortcut is taken.
- Many apps (Chrome, VS Code, Word) also use `Ctrl + Shift + V` for "paste as plain text". ClipStack's global
  shortcut takes precedence while it runs. Choose another one in Settings if you rely on that.

## Not yet implemented (post-MVP in the spec)

Import/export, auto-update, image extras (save to disk, OCR), and a per-item custom title.
