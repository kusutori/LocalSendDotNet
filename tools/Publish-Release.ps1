<#
.SYNOPSIS
    Bump Tonarink app or LocalSendDotNet.Core versions, commit, tag, and push a release.

.DESCRIPTION
    Application releases update the app csproj and Package.appxmanifest, commit
    "Ship <version>", and push tag app-vMAJOR.MINOR.PATCH so App GitHub Release
    runs. Core releases update the Core csproj and Directory.Packages.props,
    require a matching CHANGELOG heading, and push tag v<version> for nuget.org.

.EXAMPLE
    ./tools/Publish-Release.ps1
    Bump the app patch version (0.1.6 -> 0.1.7), commit, tag, and push.

.EXAMPLE
    ./tools/Publish-Release.ps1 -Bump Minor -DryRun
    Show the next minor app version without writing files.

.EXAMPLE
    ./tools/Publish-Release.ps1 -Version 0.2.0 -NoPush
    Set the app version, commit, and tag locally.

.EXAMPLE
    ./tools/Publish-Release.ps1 -Project Core -Version 0.2.0-preview.4
    Ship a Core NuGet version after CHANGELOG.md already has that heading.
#>
[CmdletBinding(DefaultParameterSetName = "Bump")]
param(
    [ValidateSet("App", "Core")]
    [string]$Project = "App",

    [Parameter(ParameterSetName = "Version")]
    [string]$Version,

    [Parameter(ParameterSetName = "Bump")]
    [ValidateSet("Major", "Minor", "Patch")]
    [string]$Bump = "Patch",

    [switch]$DryRun,
    [switch]$NoPush,
    [switch]$AllowBranch
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$appProject = Join-Path $repoRoot "src\Tonarink.App\Tonarink.App.csproj"
$appManifest = Join-Path $repoRoot "src\Tonarink.App\Package.appxmanifest"
$coreProject = Join-Path $repoRoot "src\LocalSendDotNet.Core\LocalSendDotNet.Core.csproj"
$packageProps = Join-Path $repoRoot "Directory.Packages.props"
$changelog = Join-Path $repoRoot "CHANGELOG.md"
$defaultBranch = "main"

function Read-TextFile([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = [System.Text.UTF8Encoding]::new($hasBom)
    [pscustomobject]@{
        Path = $Path
        Text = $encoding.GetString($bytes)
        Encoding = $encoding
    }
}

function Write-TextFile($File, [string]$Text) {
    [System.IO.File]::WriteAllText($File.Path, $Text, $File.Encoding)
}

function Get-CsprojVersion([string]$Path) {
    $text = (Read-TextFile $Path).Text
    $match = [regex]::Match($text, "<Version>([^<]+)</Version>")
    if (-not $match.Success) {
        throw "No <Version> element in '$Path'."
    }

    return $match.Groups[1].Value.Trim()
}

function Set-CsprojVersion([string]$Path, [string]$NewVersion) {
    $file = Read-TextFile $Path
    $updated = [regex]::Replace($file.Text, "<Version>[^<]+</Version>", "<Version>$NewVersion</Version>", 1)
    if ($updated -eq $file.Text) {
        throw "Did not update <Version> in '$Path'."
    }

    if (-not $DryRun) {
        Write-TextFile $file $updated
    }
}

function Set-ManifestVersion([string]$Path, [string]$MsixVersion) {
    $file = Read-TextFile $Path
    $updated = [regex]::Replace(
        $file.Text,
        '(<Identity\b[^>]*\bVersion=")[^"]+(")',
        { param($match) $match.Groups[1].Value + $MsixVersion + $match.Groups[2].Value },
        1)
    if ($updated -eq $file.Text) {
        throw "Did not update Package Identity Version in '$Path'."
    }

    if (-not $DryRun) {
        Write-TextFile $file $updated
    }
}

function Set-PackagePropsCoreVersion([string]$Path, [string]$NewVersion) {
    $file = Read-TextFile $Path
    $updated = [regex]::Replace(
        $file.Text,
        '(<PackageVersion Include="LocalSendDotNet.Core" Version=")[^"]+(")',
        { param($match) $match.Groups[1].Value + $NewVersion + $match.Groups[2].Value },
        1)
    if ($updated -eq $file.Text) {
        throw "Did not update LocalSendDotNet.Core package version in '$Path'."
    }

    if (-not $DryRun) {
        Write-TextFile $file $updated
    }
}

function ConvertTo-NumericVersion([string]$Text) {
    if ($Text -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "App version '$Text' must be MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH.REVISION."
    }

    $parts = @($Text.Split('.') | ForEach-Object { [int]$_ })
    foreach ($part in $parts) {
        if ($part -lt 0 -or $part -gt 65535) {
            throw "Every version component must be between 0 and 65535: '$Text'."
        }
    }

    return ,$parts
}

function ConvertTo-MsixVersion([int[]]$Parts) {
    $msix = [int[]]$Parts.Clone()
    if ($msix.Count -eq 3) {
        $msix += 0
    }

    return ($msix -join ".")
}

function Get-NextNumericVersion([int[]]$Parts, [string]$Kind) {
    $next = [int[]]$Parts.Clone()
    switch ($Kind) {
        "Major" { $next[0]++; $next[1] = 0; $next[2] = 0; if ($next.Count -gt 3) { $next[3] = 0 } }
        "Minor" { $next[1]++; $next[2] = 0; if ($next.Count -gt 3) { $next[3] = 0 } }
        "Patch" {
            if ($next.Count -gt 3 -and $next[3] -gt 0) {
                $next[3]++
            }
            else {
                $next[2]++
                if ($next.Count -gt 3) {
                    $next[3] = 0
                }
            }
        }
    }

    if ($next.Count -gt 3 -and $next[3] -eq 0) {
        $next = $next[0..2]
    }

    return ($next -join ".")
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFail
    )

    Push-Location -LiteralPath $repoRoot
    try {
        $output = & git @Arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if (-not $AllowFail -and $code -ne 0) {
        throw "git $($Arguments -join ' ') failed ($code): $output"
    }

    return [pscustomobject]@{
        ExitCode = $code
        Output = ($output | Out-String).Trim()
    }
}

if ($Project -eq "Core" -and $PSCmdlet.ParameterSetName -ne "Version") {
    throw "Core releases require -Version because they may include a prerelease label."
}

$git = Invoke-Git -Arguments @("rev-parse", "--is-inside-work-tree") -AllowFail
if ($git.ExitCode -ne 0 -or $git.Output -ne "true") {
    throw "Run this script from the LocalSendDotNet git repository."
}

$branch = (Invoke-Git -Arguments @("rev-parse", "--abbrev-ref", "HEAD")).Output
if (-not $DryRun -and -not $AllowBranch -and $branch -ne $defaultBranch) {
    throw "Current branch is '$branch'. Check out $defaultBranch or pass -AllowBranch."
}

$status = (Invoke-Git -Arguments @("status", "--porcelain")).Output
if (-not $DryRun -and $status) {
    throw "Working tree is not clean. Commit or stash other changes first.`n$status"
}

$files = @()
$commitMessage = $null
$tagName = $null
$tagMessage = $null
$newVersion = $null

if ($Project -eq "App") {
    $current = Get-CsprojVersion $appProject
    $currentParts = ConvertTo-NumericVersion $current
    if ($PSCmdlet.ParameterSetName -eq "Version") {
        $newVersion = $Version.Trim()
        if ($newVersion.StartsWith("app-", [StringComparison]::OrdinalIgnoreCase)) {
            $newVersion = $newVersion.Substring(4)
        }
        if ($newVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
            $newVersion = $newVersion.Substring(1)
        }
    }
    else {
        $newVersion = Get-NextNumericVersion $currentParts $Bump
    }

    $newParts = ConvertTo-NumericVersion $newVersion
    $msixVersion = ConvertTo-MsixVersion $newParts
    if ($newVersion -eq $current) {
        throw "App is already version $current."
    }

    $files = @($appProject, $appManifest)
    $commitMessage = "Ship $newVersion"
    $tagName = "app-v$newVersion"
    $tagMessage = "Tonarink release $tagName"

    Write-Host "App  $current -> $newVersion  (MSIX $msixVersion)"
    Write-Host "Tag  $tagName"
}
else {
    $current = Get-CsprojVersion $coreProject
    $newVersion = $Version.Trim()
    if ($newVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $newVersion = $newVersion.Substring(1)
    }

    if ($newVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z\.\-]+)?$') {
        throw "Core version '$newVersion' must be a NuGet version such as 0.2.0 or 0.2.0-preview.4."
    }

    if ($newVersion -eq $current) {
        throw "Core is already version $current."
    }

    $heading = (Get-Content -LiteralPath $changelog -Raw)
    if ($heading -notmatch "(?m)^## $($newVersion.Replace('.', '\.'))\s*$") {
        throw "CHANGELOG.md has no '## $newVersion' heading. Add release notes first."
    }

    $files = @($coreProject, $packageProps)
    $commitMessage = "Ship Core $newVersion"
    $tagName = "v$newVersion"
    $tagMessage = "LocalSendDotNet.Core $newVersion"

    Write-Host "Core $current -> $newVersion"
    Write-Host "Tag  $tagName"
}

Write-Host "Files:"
$files | ForEach-Object { Write-Host "  $_" }

if ($DryRun) {
    Write-Host "Dry run: no files, commit, tag, or push."
    return
}

$localTag = Invoke-Git -Arguments @("rev-parse", "-q", "--verify", "refs/tags/$tagName") -AllowFail
if ($localTag.ExitCode -eq 0) {
    throw "Local tag '$tagName' already exists."
}

if (-not $NoPush) {
    $remoteTag = Invoke-Git -Arguments @("ls-remote", "--tags", "origin", "refs/tags/$tagName") -AllowFail
    if ($remoteTag.ExitCode -eq 0 -and $remoteTag.Output) {
        throw "Remote tag '$tagName' already exists."
    }
}

if ($Project -eq "App") {
    Set-CsprojVersion $appProject $newVersion
    Set-ManifestVersion $appManifest $msixVersion
}
else {
    Set-CsprojVersion $coreProject $newVersion
    Set-PackagePropsCoreVersion $packageProps $newVersion
}

$relativeFiles = foreach ($file in $files) {
    [System.IO.Path]::GetRelativePath($repoRoot, $file)
}

Invoke-Git -Arguments (@("add") + $relativeFiles) | Out-Null
$staged = (Invoke-Git -Arguments @("diff", "--cached", "--name-only")).Output
if (-not $staged) {
    throw "Nothing staged after updating version files."
}

Invoke-Git -Arguments @("commit", "-m", $commitMessage) | Out-Null
Invoke-Git -Arguments @("tag", "-a", $tagName, "-m", $tagMessage) | Out-Null
Write-Host "Created commit and tag $tagName."

if ($NoPush) {
    Write-Host "Skipped push. Publish later with:"
    Write-Host "  git push origin $branch"
    Write-Host "  git push origin $tagName"
    return
}

Invoke-Git -Arguments @("push", "origin", $branch) | Out-Null
Invoke-Git -Arguments @("push", "origin", $tagName) | Out-Null
Write-Host "Pushed $branch and $tagName."
if ($Project -eq "App") {
    Write-Host "Release workflow: https://github.com/kusutori/LocalSendDotNet/actions"
}
else {
    Write-Host "NuGet publish workflow: https://github.com/kusutori/LocalSendDotNet/actions"
}
