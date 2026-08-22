# Windows publishing and packaging

The Reactor app keeps its normal Debug and Release builds unpackaged. Native AOT
and MSIX are explicit release shapes so packaging does not slow down the inner
development loop.

## Native AOT

Publish the x64 build with the checked-in profile:

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj `
  -p:PublishProfile=win-x64-aot
```

For Windows on ARM64, use `win-arm64-aot` instead. Outputs are written under
`artifacts/publish/native-aot/<rid>/`.

The equivalent one-off command is:

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 -p:NativeAot=true `
  -o artifacts/publish/native-aot/win-x64
```

Native AOT keeps `InvariantGlobalization` off. `LocaleProvider` constructs a
`CultureInfo` for `zh-CN` / `en-US` when loading Resw strings; invariant mode
throws `CultureNotFoundException` as soon as the shell mounts. Language
*detection* still uses the WinRT `GlobalizationPreferences` API rather than
`CultureInfo.CurrentUICulture`. Reactor DevTools remains Debug-only and is
therefore absent from the trimmed retail binary.

## MSIX

The application uses single-project MSIX packaging, following ReactorGallery. The
default build still has `WindowsPackageType=None`; opt into packaging with:

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Release `
  -p:Platform=x64 `
  -p:TonarinkPackaged=true `
  -p:GenerateAppxPackageOnBuild=true
```

This produces an unsigned package under the app project's `AppPackages` directory.
It is suitable for validating the package layout, but Windows will not install it
until it is signed.

The default package does **not** include Windows 11 widgets. To build the larger
widgets flavor (same package identity, includes `Tonarink.WidgetProvider`):

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Release `
  -p:Platform=x64 `
  -p:TonarinkPackaged=true `
  -p:TonarinkWidgets=true `
  -p:GenerateAppxPackageOnBuild=true
```

For a sideloadable build, set `Package.appxmanifest`'s `Identity Publisher` to the
exact subject of the signing certificate and add:

```powershell
-p:AppxPackageSigningEnabled=true `
-p:PackageCertificateKeyFile=C:\path\LocalSendDotNet.pfx
```

Keep the PFX and its password outside the repository. The manifest declares both
`internetClientServer` and `privateNetworkClientServer`, which the LocalSend HTTP
server and local-network discovery require, plus `runFullTrust` for the desktop app.

For local sideload testing, generate a compatible code-signing certificate with:

```powershell
.\tools\New-MsixSigningCertificate.ps1
```

The generated certificate contains the Basic Constraints extension required by
Visual Studio's `Add-AppDevPackage.ps1`, with `CA=false`, plus the Code Signing EKU
and Digital Signature key usage. Copy the generated `.cer` beside the `.msix` only
when using the generated installation script. The package must be signed with the
matching private key from `Cert:\CurrentUser\My` (or an exported PFX).

## Native AOT inside MSIX

Native AOT and MSIX are independent choices. Do **not** pass
`TonarinkPackaged=true` together with `NativeAot=true` on the same
`dotnet publish`: the MSIX targets then package the managed apphost (~600 KB)
instead of the native executable (~26 MB), and the resulting app white-screens
and crashes.

Publish the unpackaged Native AOT layout first, copy the stamped
`Package.appxmanifest` in as `AppxManifest.xml`, set its processor architecture
and replace the generated `$targetentrypoint$` placeholder with
`Windows.FullTrustApplication`, copy PNG/ICO assets, then build `resources.pri`
with `tools/New-AotMsixResourcesPri.ps1` before packing. The unpackaged AOT
publish only emits `Tonarink.pri`; Windows Shell reads package logos from
`resources.pri`. Without it the taskbar falls back to a plated
`Square44x44Logo`. That PRI must be a full merged resource map (app + WinUI +
qualified assets), named after the package identity. A PNG-only
`resources.pri` becomes the package primary map and WinUI fail-fasts at
startup (`Microsoft.UI.Xaml.dll`, `0xc000027b`) before any window appears.
Windows 11 widgets are **not** in the default package. They add a
self-contained COM host under `WidgetProvider\` (~90 MB). Opt in with
`-p:TonarinkWidgets=true` on a packaged build; that injects
`Package.Widgets.extensions.xml` into the manifest and copies the provider.
Do not use `winapp package` for this layout.

```powershell
dotnet publish src/Tonarink.App/Tonarink.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 -p:NativeAot=true `
  -p:TonarinkPackaged=false -p:WindowsPackageType=None `
  -o artifacts/publish/native-aot/win-x64

Copy-Item src/Tonarink.App/Package.appxmanifest `
  artifacts/publish/native-aot/win-x64/AppxManifest.xml
Copy-Item src/Tonarink.App/Assets/*.png, src/Tonarink.App/Assets/*.ico `
  artifacts/publish/native-aot/win-x64/Assets
./tools/New-AotMsixResourcesPri.ps1 `
  -LayoutPath artifacts/publish/native-aot/win-x64

makeappx pack /o /d artifacts/publish/native-aot/win-x64 `
  /p artifacts/Tonarink-win-x64-aot.msix
```

A missing `mspdbcmf.exe` only prevents generation of the optional symbol package; it
does not prevent the `.msix` application package from being created.

## GitHub Releases

Pushing a numeric `app-vMAJOR.MINOR.PATCH` tag builds x64 and ARM64 Native AOT
portable archives, signed managed MSIX sideload ZIPs, and signed Native AOT MSIX
sideload ZIPs, then attaches them to the GitHub Release for that tag. Managed
MSIX ZIPs use the standard `*_Test` AppPackages layout; Native AOT MSIX ZIPs use
the repository's lightweight sideload installer. The separate tag prefix avoids
triggering Core NuGet publication. The private key is supplied only through
GitHub Actions Secrets. See [app-release-ci.md](app-release-ci.md) for setup and
release instructions.
