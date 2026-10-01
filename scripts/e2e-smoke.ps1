<#
  End-to-end smoke test of the real user workflow, on the real desktop.
  Requires an unlocked, interactive session. Don't touch the keyboard/mouse while it runs (~20 s).

  1. Starts ClipStack with a throwaway profile (your real history/settings are untouched).
  2. Copies text, a duplicate, secrets (must be ignored), an image and files.
  3. Opens a plain text window, presses Ctrl+Shift+V, searches, presses Enter.
  4. Verifies the right text was pasted into that window, then tests Shift+Enter, Ctrl+P and Delete.

  Usage:  powershell -ExecutionPolicy Bypass -File scripts\e2e-smoke.ps1 [-Exe path\to\ClipStack.exe]
#>
param([string]$Exe = "$PSScriptRoot\..\src\ClipStack\bin\Debug\net9.0-windows\ClipStack.exe")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class E2E {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void Combo(params byte[] ks) { foreach (var k in ks) keybd_event(k, 0, 0, UIntPtr.Zero); for (int i = ks.Length - 1; i >= 0; i--) keybd_event(ks[i], 0, 2, UIntPtr.Zero); }
}
"@
[void][E2E]::SetProcessDPIAware()

$CTRL = 0x11; $SHIFT = 0x10; $ENTER = 0x0D; $ESC = 0x1B; $DOWN = 0x28; $DEL = 0x2E
$V = 0x56; $P = 0x50

$work = Join-Path $env:TEMP ("ClipStackE2E-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$targetOut = Join-Path $work "target.txt"
'{ "StartWithWindows": false, "FirstRunCompleted": true, "EnableLogging": true, "Placement": "ScreenCenter" }' |
  Out-File -Encoding utf8 (Join-Path $work "settings.json")

$failures = 0
function Check([bool]$ok, [string]$what) {
  if ($ok) { Write-Host "  PASS  $what" -ForegroundColor Green }
  else { Write-Host "  FAIL  $what" -ForegroundColor Red; $script:failures++ }
}
function Copy-Text([string]$t) { Set-Clipboard -Value $t; Start-Sleep -Milliseconds 400 }
function Send-Keys([byte[]]$keys) { [E2E]::Combo($keys); Start-Sleep -Milliseconds 150 }
function Type-Text([string]$text) {
  foreach ($ch in $text.ToUpper().ToCharArray()) { [E2E]::Combo([byte[]]@([byte][char]$ch)); Start-Sleep -Milliseconds 40 }
  Start-Sleep -Milliseconds 200
}
function Popup-Visible { $h = [E2E]::FindWindow([NullString]::Value, "ClipStack"); $h -ne [IntPtr]::Zero -and [E2E]::IsWindowVisible($h) }
function Target-Text { Start-Sleep -Milliseconds 500; if (Test-Path $targetOut) { [IO.File]::ReadAllText($targetOut) } else { "" } }
function Focus-Target { [void][E2E]::SetForegroundWindow($script:targetHwnd); Start-Sleep -Milliseconds 300 }
function Open-Popup { Focus-Target; Send-Keys @($CTRL, $SHIFT, $V); Start-Sleep -Milliseconds 400 }

$env:CLIPSTACK_DATA_DIR = $work
Get-Process ClipStack -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Resolve-Path $Exe).Path } | Stop-Process -Force
$app = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 2

# A plain text window standing in for the user's editor; mirrors its text to a file.
$targetScript = @"
Add-Type -AssemblyName System.Windows.Forms
`$f = New-Object System.Windows.Forms.Form; `$f.Text = 'PasteTarget'; `$f.Width = 500; `$f.Height = 300
`$t = New-Object System.Windows.Forms.TextBox; `$t.Multiline = `$true; `$t.Dock = 'Fill'; `$f.Controls.Add(`$t)
`$tm = New-Object System.Windows.Forms.Timer; `$tm.Interval = 150; `$tm.Add_Tick({ [IO.File]::WriteAllText('$targetOut', `$t.Text) }); `$tm.Start()
[void]`$f.ShowDialog()
"@
$target = Start-Process powershell -ArgumentList "-NoProfile", "-Command", $targetScript -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
$script:targetHwnd = [E2E]::FindWindow([NullString]::Value, "PasteTarget")

try {
  Write-Host "Capture"
  Copy-Text "Meeting notes: tomorrow at 10:00"
  Copy-Text "https://github.com/user/project"
  Copy-Text "def hello():`r`n    print(`"Hello`")"
  Copy-Text "q7#Vz!9pLm`$2"                      # password-like: ignored
  Copy-Text "AKIAIOSFODNN7EXAMPLE"               # AWS key: ignored
  Copy-Text "https://github.com/user/project"    # duplicate: moves to top
  $bmp = New-Object System.Drawing.Bitmap 64, 48
  [System.Drawing.Graphics]::FromImage($bmp).Clear([System.Drawing.Color]::SteelBlue)
  [System.Windows.Forms.Clipboard]::SetImage($bmp); Start-Sleep -Milliseconds 600
  $files = New-Object System.Collections.Specialized.StringCollection
  [void]$files.Add("C:\Windows\win.ini"); [void]$files.Add("C:\Windows\system.ini")
  [System.Windows.Forms.Clipboard]::SetFileDropList($files); Start-Sleep -Milliseconds 600
  Copy-Text "Last thing copied"

  $log = Get-Content (Join-Path $work "clipstack.log") -Raw
  Check (([regex]::Matches($log, "Captured Text")).Count -eq 5) "5 text captures (incl. 1 duplicate), secrets skipped"
  Check ($log -match "Skipped sensitive clipboard content \(password\)") "password ignored"
  Check ($log -match "Skipped sensitive clipboard content \(API token\)") "API key ignored"
  Check ($log -match "copies: 2") "duplicate bumped instead of re-added"
  Check ($log -match "Captured Image") "image captured"
  Check ($log -match "Captured Files") "files captured"

  Write-Host "Popup + search + paste"
  Open-Popup
  Check (Popup-Visible) "Ctrl+Shift+V opens the popup"
  Type-Text "meeting"
  Send-Keys @($ENTER)
  Check (-not (Popup-Visible)) "Enter closes the popup"
  Check ((Target-Text) -eq "Meeting notes: tomorrow at 10:00") "searched item pasted into the previous window"

  Open-Popup
  Send-Keys @($ESC)
  Check (-not (Popup-Visible)) "Esc closes without pasting"
  Check ((Target-Text) -eq "Meeting notes: tomorrow at 10:00") "nothing pasted on Esc"

  Write-Host "Keyboard navigation"
  Open-Popup
  Send-Keys @($DOWN)          # 2nd item: the pasted "Meeting notes" moved to the top, so this is "Last thing copied"
  Send-Keys @($ENTER)
  Check ((Target-Text).EndsWith("Last thing copied")) "Down + Enter pastes the second item"

  Write-Host "Pin and delete"
  Open-Popup
  Type-Text "github"
  Send-Keys @($CTRL, $P)
  Send-Keys @($ESC)
  Open-Popup
  Send-Keys @($ENTER)         # pinned item is first when the search is empty
  Check ((Target-Text).EndsWith("https://github.com/user/project")) "pinned item shown first"
  Open-Popup
  Type-Text "hello"
  Send-Keys @($DEL)
  Send-Keys @($ESC)
  Open-Popup
  Type-Text "hello"
  Send-Keys @($ENTER)
  Check (-not (Target-Text).Contains("print")) "deleted item is gone"
  Send-Keys @($ESC)

  Write-Host "Persistence"
  Stop-Process -Id $app.Id -Force; Start-Sleep -Seconds 1
  $app = Start-Process -FilePath $Exe -PassThru; Start-Sleep -Seconds 2
  $log = Get-Content (Join-Path $work "clipstack.log") -Raw
  # meeting, last thing, files, image, github (the "hello" snippet was deleted)
  Check ($log -match "Started with 5 item") "history survives a restart"
}
finally {
  Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
  Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($failures -eq 0) { Write-Host "`nAll checks passed." -ForegroundColor Green; exit 0 }
Write-Host "`n$failures check(s) failed." -ForegroundColor Red; exit 1
