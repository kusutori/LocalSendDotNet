# Regenerates Splash/SplashLogo.cs from Assets/SplashLogo.json.
# Requires the LottieGen tool (net9+). On machines with only net10:
#   $env:DOTNET_ROLL_FORWARD = "LatestMajor"
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root "artifacts\tools\LottieGen.exe"
if (-not (Test-Path $tool)) {
    New-Item -ItemType Directory -Path (Split-Path $tool) -Force | Out-Null
    dotnet tool install lottiegen --version 8.2.250604 --tool-path (Split-Path $tool)
}

$env:DOTNET_ROLL_FORWARD = "LatestMajor"
$outDir = Join-Path $root "src\LocalSendDotNet.App\Splash"
& $tool `
    -InputFile (Join-Path $root "src\LocalSendDotNet.App\Assets\SplashLogo.json") `
    -Language cs `
    -Namespace LocalSendDotNet `
    -OutputFolder $outDir `
    -Public `
    -WinUIVersion 3.0

# CsWinRT AOT requires WinRT interface implementers to be partial.
$path = Join-Path $outDir "SplashLogo.cs"
$text = Get-Content -LiteralPath $path -Raw
$text = $text.Replace("public sealed class SplashLogo", "public sealed partial class SplashLogo")
$text = $text.Replace("sealed class SplashLogo_AnimatedVisual", "sealed partial class SplashLogo_AnimatedVisual")
Set-Content -LiteralPath $path -Value $text -NoNewline
