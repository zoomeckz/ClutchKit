# Builds ClutchKit from source - the same way on your PC and on GitHub.
#   powershell -ExecutionPolicy Bypass -File build.ps1
# Output in .\dist:  ClutchKit.exe  and  ClutchKit-<version>.zip (exe + install guide)
# Needs only Windows (the .NET Framework C# compiler ships with Windows).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'
Remove-Item $dist, $obj -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist, $obj | Out-Null

# version comes from the source code
$code = Get-Content (Join-Path $root 'src\ClutchKit.cs') -Raw
$version = [regex]::Match($code, 'public const string Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Could not find the version in src\ClutchKit.cs' }
Write-Host "Building ClutchKit $version"

# 1) app icon, drawn from code
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1') -Out (Join-Path $obj 'ClutchKit') -Style stack
if ($LASTEXITCODE) { throw 'Icon build failed' }

# 2) compile
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $obj
try {
    & $csc /nologo /target:winexe /optimize+ /win32icon:ClutchKit.ico /resource:ClutchKit.ico,ClutchKit.ico `
        "/out:$dist\ClutchKit.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $root 'src\ClutchKit.cs')
    if ($LASTEXITCODE) { throw 'Compile failed' }
}
finally { Pop-Location }

# 3) package: exe + install guide
$zip = Join-Path $dist "ClutchKit-$version.zip"
Compress-Archive -Path "$dist\ClutchKit.exe", (Join-Path $root 'docs\How to install ClutchKit.txt') -DestinationPath $zip -Force
Write-Host "Done: $dist\ClutchKit.exe and $zip"
