<#
  Builds a release copy of ClipStack into .\dist.
    -SelfContained   bundle the .NET runtime (bigger, ~70 MB, but runs on PCs without .NET 9 Desktop Runtime)
#>
param([switch]$SelfContained)

$root = Resolve-Path "$PSScriptRoot\.."
$out = Join-Path $root "dist"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue

dotnet publish "$root\src\ClipStack\ClipStack.csproj" -c Release -r win-x64 -o $out `
  --self-contained:$($SelfContained.IsPresent) `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem $out | Format-Table Name, @{ n = "Size (MB)"; e = { [math]::Round($_.Length / 1MB, 1) } }
Write-Host "Run $out\ClipStack.exe"
