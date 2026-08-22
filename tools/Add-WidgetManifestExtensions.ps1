<#
.SYNOPSIS
    Copy Package.appxmanifest and append the Widgets COM / AppExtension markup.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [string]$FragmentPath
)

$ErrorActionPreference = "Stop"

$sourcePath = [IO.Path]::GetFullPath($Source)
$destinationPath = [IO.Path]::GetFullPath($Destination)
if ([string]::IsNullOrWhiteSpace($FragmentPath)) {
    $FragmentPath = Join-Path (Split-Path $sourcePath -Parent) "Package.Widgets.extensions.xml"
}
$fragmentPath = [IO.Path]::GetFullPath($FragmentPath)

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Manifest '$sourcePath' was not found."
}
if (-not (Test-Path -LiteralPath $fragmentPath)) {
    throw "Widget fragment '$fragmentPath' was not found."
}

$packageNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"
$uap3Ns = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3"

[xml]$package = Get-Content -LiteralPath $sourcePath -Raw
$nsmgr = New-Object Xml.XmlNamespaceManager($package.NameTable)
$nsmgr.AddNamespace("def", $packageNs)
$nsmgr.AddNamespace("uap3", $uap3Ns)

$existing = $package.SelectSingleNode("//uap3:AppExtension[@Id='Tonarink.Widgets']", $nsmgr)
if ($null -eq $existing) {
    $root = $package.DocumentElement
    if (-not $root.HasAttribute("xmlns:uap3")) {
        $root.SetAttribute("xmlns:uap3", $uap3Ns)
    }

    $ignorable = $root.GetAttribute("IgnorableNamespaces")
    if ($ignorable -notmatch '(^|\s)uap3(\s|$)') {
        $root.SetAttribute("IgnorableNamespaces", (($ignorable + " uap3").Trim()))
    }

    $extensions = $package.SelectSingleNode("//def:Applications/def:Application/def:Extensions", $nsmgr)
    if ($null -eq $extensions) {
        throw "Package.appxmanifest is missing Application/Extensions."
    }

    [xml]$fragment = Get-Content -LiteralPath $fragmentPath -Raw
    foreach ($child in @($fragment.DocumentElement.ChildNodes)) {
        if ($child.NodeType -ne [Xml.XmlNodeType]::Element) {
            continue
        }
        $imported = $package.ImportNode($child, $true)
        [void]$extensions.AppendChild($imported)
    }
}

$directory = Split-Path $destinationPath -Parent
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($destinationPath, $settings)
try {
    $package.Save($writer)
}
finally {
    $writer.Dispose()
}

Get-Item -LiteralPath $destinationPath
