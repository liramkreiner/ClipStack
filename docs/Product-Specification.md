# Windows Clipboard Manager — Product Specification

## 1. Product Overview

Build a lightweight, fast, privacy-focused **Windows clipboard manager**, inspired by the workflow and simplicity of popular macOS clipboard managers.

The application runs in the background and continuously monitors the Windows clipboard. Every copied item is stored in a local clipboard history that the user can quickly access using a global keyboard shortcut.

The primary goal is:

> **Copy anything → press a shortcut → instantly find and paste what you copied earlier.**

The application should feel like a native Windows utility rather than a large Electron-style application.

---

# 2. Core User Experience

The application runs silently in the background.

When the user copies something:

1. Detect the clipboard change.
2. Store the clipboard item locally.
3. Add it to the clipboard history.
4. Keep the application out of the user's way.

When the user presses the global shortcut, for example:

**Ctrl + Shift + V**

a small clipboard-history window appears near the mouse cursor or active application.

The user can:

- Start typing immediately to search.
- Navigate with ↑ / ↓.
- Press Enter to paste.
- Press Esc to close.
- Use keyboard shortcuts to pin, delete, or copy an item.
- Click an item with the mouse.

The entire interaction should be extremely fast.

---

# 3. Clipboard History

Store previously copied items locally.

Each history item should contain:

- Unique ID
- Content type
- Content
- Timestamp
- Application it was copied from, if available
- Pinned status
- Copy count / usage count
- Optional title/preview
- Optional metadata

Supported clipboard types:

### Text

Examples:

```text
Hello world
```

URLs:

```text
https://example.com
```

Code:

```python
def hello():
    print("Hello")
```

Multiline text should be supported.

### Images

Support common image clipboard formats:

- PNG
- JPEG
- Bitmap
- Other Windows clipboard image formats where practical

Display a thumbnail in the history.

### Files

Support copied files/folders from Windows Explorer.

For example:

```text
C:\Users\Liram\Documents\report.pdf
```

Multiple files should be treated as a single clipboard item.

---

# 4. Duplicate Handling

If the user copies exactly the same content multiple times, don't create unnecessary duplicates.

Instead:

- Move the existing item to the top.
- Update its timestamp.
- Increment its usage/copy count.

Pinned items should remain pinned even when their position changes.

There should optionally be a setting allowing duplicate entries.

---

# 5. Search

Search should be available immediately when the clipboard window opens.

The user can simply start typing.

Example:

History:

```text
Meeting notes
GitHub repository
https://github.com/...
Python code
Apartment contract
```

Typing:

```text
github
```

should immediately filter the list.

Search should support:

- Case-insensitive matching
- Partial matching
- Multiline text
- URLs
- File names
- Fuzzy search if practical

Search results should be ranked intelligently.

Suggested ranking:

1. Exact match
2. Prefix match
3. Fuzzy match
4. Recently used items

Search must feel instant even with thousands of clipboard items.

---

# 6. Keyboard Navigation

Keyboard-first interaction is extremely important.

Default shortcut:

**Ctrl + Shift + V**

Open clipboard history.

Inside the window:

- `↑` / `↓` — navigate
- `Enter` — paste selected item
- `Esc` — close
- `Ctrl + Enter` — optionally paste without closing
- `Delete` — delete selected item
- `Ctrl + P` — pin/unpin
- `Ctrl + F` — focus search
- `Ctrl + C` — copy selected item back to clipboard
- `Page Up / Page Down` — navigate faster
- `Home / End` — first/last item

The exact shortcuts should be configurable.

---

# 7. Paste Behavior

Selecting an item should normally:

1. Put the selected clipboard item into the Windows clipboard.
2. Close the clipboard window.
3. Paste it into the previously focused application.

Ideally the application should simulate:

```text
Ctrl + V
```

after restoring focus to the original application.

The clipboard manager must avoid accidentally pasting into itself.

For example:

User is typing in VS Code:

```text
Ctrl + Shift + V
```

Clipboard manager opens.

User selects:

```text
def hello():
    print("Hello")
```

Presses Enter.

Expected result:

```text
def hello():
    print("Hello")
```

appears directly in VS Code.

---

# 8. Clipboard Window

The UI should be small and minimal.

Conceptually:

```text
┌───────────────────────────────────────┐
│ 🔍 Search clipboard...                │
├───────────────────────────────────────┤
│                                       │
│  📌 GitHub repository                 │
│     https://github.com/...            │
│                                       │
│  Python function                      │
│     def calculate_total(...           │
│                                       │
│  Meeting notes                        │
│     Tomorrow's meeting at 10:00...    │
│                                       │
│  📷 Image                             │
│     [thumbnail]                       │
│                                       │
└───────────────────────────────────────┘
```

The window should:

- Have no normal Windows taskbar presence.
- Stay above other windows while open.
- Close when pressing Esc.
- Close when clicking outside.
- Have rounded corners on modern Windows.
- Support light and dark mode.
- Follow the Windows system theme by default.

---

# 9. Item Preview

Each clipboard item should display a useful preview.

For text:

```text
Python function to calculate...
```

For URLs:

```text
🌐 github.com/user/project
```

For images:

```text
[Image thumbnail]
```

For files:

```text
📄 report.pdf
```

For multiple files:

```text
📁 4 files
```

Long content should be truncated in the list but remain fully accessible.

---

# 10. Pinning / Favorites

Users should be able to pin frequently used clipboard items.

Pinned items should survive history cleanup.

Examples:

- Email signature
- Frequently used commands
- SSH commands
- Code snippets
- Addresses
- Phone numbers
- Templates

Pinned items should appear in a dedicated section at the top.

Example:

```text
📌 PINNED

Git command
Email signature
Company address

──────────────

RECENT

Meeting notes
Python code
Screenshot
```

---

# 11. History Limits

Settings should allow the user to choose the maximum number of stored items.

Examples:

- 50
- 100
- 500
- 1,000
- 5,000
- 10,000
- Unlimited

The default should be reasonable, such as 500 or 1,000 items.

When the limit is reached:

- Remove oldest unpinned items first.
- Never automatically remove pinned items.

---

# 12. Automatic Cleanup

Provide settings for automatically deleting clipboard history older than:

- 1 hour
- 1 day
- 7 days
- 30 days
- Never

Pinned items should be excluded unless the user explicitly chooses otherwise.

---

# 13. Privacy

Privacy is a major feature.

Clipboard contents can contain extremely sensitive information:

- Passwords
- API keys
- Credit cards
- Authentication codes
- Personal information

Therefore:

### Local-first architecture

Clipboard history should be stored locally.

Do NOT send clipboard contents to a server.

Do NOT require an account.

Do NOT use cloud synchronization by default.

Do NOT include analytics containing clipboard content.

---

# 14. Sensitive Clipboard Detection

Optionally detect potentially sensitive clipboard content.

Examples:

- Password-like strings
- Credit card numbers
- API keys
- JWTs
- Authentication codes
- Private keys

Provide a setting:

**Automatically ignore sensitive clipboard contents**

When enabled, sensitive data should not be saved.

This detection should happen locally.

Never send the clipboard to an external AI/API merely to determine whether it is sensitive.

---

# 15. Application Exclusions

Allow the user to exclude specific applications from clipboard history.

Example:

```text
Never save clipboard contents from:

☑ Password managers
☑ Banking applications
☐ Chrome
☐ VS Code
☐ Slack
```

The user should be able to add applications manually.

Possible configuration:

```text
Excluded Applications

1Password.exe
KeePass.exe
Bitwarden.exe
```

When copying from an excluded application, the clipboard should still work normally; the application simply doesn't save the content.

---

# 16. Clipboard Pause

Provide a quick way to pause clipboard monitoring.

Example tray menu:

```text
Clipboard Manager

✓ Monitoring clipboard

Pause monitoring
Clear history
Settings
Quit
```

When paused:

- Existing history remains.
- New clipboard items aren't saved.

---

# 17. Menu Bar / System Tray

The application should live in the Windows system tray.

Tray menu:

```text
Clipboard Manager

Open Clipboard
Pause Monitoring
────────────────
Clear History
────────────────
Settings
About
────────────────
Quit
```

The tray icon should indicate whether monitoring is active.

---

# 18. Global Shortcut

Allow users to configure the global shortcut.

Default:

```text
Ctrl + Shift + V
```

Possible alternatives:

```text
Ctrl + Alt + V
Win + V
```

The application should detect shortcut conflicts where possible.

---

# 19. Multiple Clipboard Actions

Right-clicking an item should provide:

```text
Paste
Copy to Clipboard
Paste as Plain Text
Pin
Unpin
Delete
Delete All Similar
Show Details
```

For text items, **Paste as Plain Text** should remove formatting.

---

# 20. Rich Text

If feasible, preserve rich clipboard formats.

For example, copying formatted text from Word should preserve:

- Bold
- Italic
- Links
- Basic formatting

However, the MVP can initially prioritize plain text and images.

---

# 21. Plain Text Conversion

For any rich-text clipboard item:

```text
Paste
Paste as Plain Text
```

should be available.

"Paste as Plain Text" should strip:

- HTML formatting
- Rich text formatting
- Styling
- Embedded metadata

while preserving the actual text.

---

# 22. Image Support

Images should have special treatment.

When an image is copied:

```text
📷
[thumbnail]
Copied 2 minutes ago
```

Clicking it should paste the image.

Potential future features:

- Save image to disk
- Copy image as file
- OCR
- Image compression
- Convert format

These should not be necessary for the initial version.

---

# 23. File Support

When copying files from Explorer:

```text
📄 report.pdf
📄 presentation.pptx
```

the clipboard manager should preserve the file references.

Pasting the item should paste the original files into the destination.

Do not duplicate the files unnecessarily unless required by Windows clipboard behavior.

---

# 24. Persistence

Clipboard history should survive application restarts.

Recommended local database:

SQLite.

Possible schema:

```text
clipboard_items
----------------
id
type
content
created_at
updated_at
last_used_at
use_count
is_pinned
source_application
content_hash
metadata
```

For images, consider storing binary data efficiently or storing references to an encrypted/local cache.

The database should be optimized for fast search.

---

# 25. Database Security

Since clipboard contents can be sensitive:

- Database must only be accessible to the current Windows user.
- Do not expose it over a network.
- Consider Windows DPAPI / Credential APIs for encryption of sensitive stored data.
- Avoid plaintext temporary files.
- Secure image cache as well.

Encryption should be configurable depending on performance requirements.

---

# 26. Performance Requirements

The application should be extremely lightweight.

Target:

- Very low idle CPU usage.
- Low memory usage.
- Clipboard capture should be asynchronous.
- UI should open almost instantly.
- Search should respond without noticeable delay.
- Large histories should remain usable.

The clipboard monitor must never block the user's workflow.

---

# 27. Startup

Provide:

**Start with Windows**

enabled by default or easily configurable.

The application should launch minimized to the system tray.

No splash screen.

No unnecessary startup window.

---

# 28. Notifications

Avoid unnecessary notifications.

Do NOT show:

> "Clipboard item saved!"

for every copy.

The application should be essentially invisible until requested.

Notifications should only be used for important events such as:

- First-run explanation
- Update available
- Database/storage issue

---

# 29. Settings

Create a clean settings interface.

Suggested categories:

### General

- Start with Windows
- Global shortcut
- Theme
- Show tray icon
- Window position

### History

- Maximum history size
- Automatic cleanup
- Duplicate handling
- Clear history

### Privacy

- Excluded applications
- Ignore sensitive content
- Encryption
- Don't store images
- Don't store files

### Appearance

- Theme
- Window size
- Preview length
- Show timestamps
- Show application icons

### Advanced

- Database location
- Logging
- Reset application

---

# 30. First Launch

On first launch, show a very short onboarding:

```text
Your clipboard, remembered.

Copy anything normally.
Press Ctrl + Shift + V to access your history.

Everything stays on this computer.

[Get Started]
```

Then minimize to the tray.

---

# 31. Contextual Application Information

Where possible, identify which application created the clipboard item.

Example:

```text
GitHub URL
Copied from Chrome
2 minutes ago
```

or:

```text
SQL query
Copied from VS Code
15 minutes ago
```

Display the application icon if possible.

This is optional metadata and should not prevent clipboard capture.

---

# 32. Usage Ranking

Track which clipboard items the user actually uses.

For example:

```text
use_count = 15
```

Frequently reused items can be ranked higher in search.

The user should be able to disable usage tracking.

---

# 33. Quick Delete

Provide:

```text
Delete
```

for individual items.

Also:

```text
Clear History
```

with confirmation.

Pinned items should be protected from "Clear History" unless the user explicitly chooses:

```text
Clear Everything Including Pinned Items
```

---

# 34. Import / Export

Optional feature.

Allow users to export their clipboard history to a local encrypted file.

Example:

```text
Export Clipboard History
```

and:

```text
Import Clipboard History
```

This can be implemented after the MVP.

---

# 35. Auto Update

The application should eventually support automatic updates.

The update mechanism should:

- Check for new versions.
- Download securely.
- Verify the update.
- Install without requiring complicated manual steps.

Do not make automatic updates dependent on cloud clipboard synchronization.

---

# 36. Architecture

Prefer a native Windows implementation.

Recommended options:

### Option A — C# / .NET

Use:

- C#
- .NET 8+ / current supported .NET
- WPF or WinUI 3
- SQLite
- Windows APIs for clipboard monitoring
- Windows global hotkey APIs
- Windows tray integration

This is likely the easiest choice for a polished Windows-native application.

### Option B — Rust

Use:

- Rust
- Windows APIs
- Tauri or a lightweight native UI
- SQLite

Use this if minimizing memory usage and maximizing low-level control are priorities.

Avoid Electron unless there is a strong reason to use it.

---

# 37. Windows Integration

Use native Windows APIs where appropriate.

Important capabilities:

- Clipboard monitoring
- Global keyboard shortcuts
- Foreground-window detection
- Application identification
- Clipboard format detection
- System tray
- Windows startup
- DPAPI/security APIs
- Native window positioning

The application should behave like a normal Windows utility.

---

# 38. MVP

The first version should NOT attempt to implement everything.

Build this first:

### MVP features

1. Background clipboard monitoring
2. Text clipboard history
3. Persistent SQLite storage
4. Global keyboard shortcut
5. Clipboard popup
6. Search
7. Keyboard navigation
8. Paste selected item
9. Duplicate detection
10. Pinning
11. Delete items
12. Clear history
13. History size limit
14. System tray
15. Start with Windows
16. Light/dark theme
17. Application exclusion
18. Privacy-first local storage

After this works reliably, add:

- Images
- Files
- Rich text
- Sensitive-data detection
- Encryption
- Import/export
- Advanced search
- Usage ranking
- Auto-update

---

# 39. UX Principle

The most important design principle is:

> **The user should never have to think about the clipboard manager.**

Normal copying must behave exactly as it does in Windows.

The application only becomes visible when the user explicitly invokes it.

The workflow should be:

```text
COPY
  ↓
Continue working
  ↓
Ctrl + Shift + V
  ↓
Search / navigate
  ↓
Enter
  ↓
PASTE
```

The entire process should feel instantaneous.

---

# 40. Definition of Done

The application is considered successful when a user can:

1. Install it.
2. Launch it.
3. Forget that it is running.
4. Copy 100+ pieces of content throughout the day.
5. Press Ctrl + Shift + V at any time.
6. Immediately search their previous clipboard contents.
7. Select an item with the keyboard.
8. Press Enter.
9. Have that content pasted into the application they were previously using.
10. Restart Windows.
11. Find their clipboard history still available.

The application should feel like a **small, native, invisible Windows utility**, not a large productivity application.

## Development instruction for the AI agent

Before implementing the complete application, build it incrementally.

First create the clipboard-monitoring core and persistence layer.

Then implement the global shortcut and popup UI.

Then implement search and paste behavior.

Then add privacy/security features.

Then add images/files and advanced functionality.

After each stage, run the application and test the complete user workflow rather than only testing individual functions.

Prioritize **correct clipboard behavior, reliability, speed, and native Windows integration over visual complexity**.

Do not copy source code from other clipboard managers. Build an independent Windows implementation.