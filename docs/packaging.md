# Windows publishing and packaging

The Reactor app keeps its normal Debug and Release builds unpackaged. Native AOT
and MSIX are explicit release shapes so packaging does not slow down the inner
development loop.

## Native AOT

Publish the x64 build with the checked-in profile:

```powershell
dotnet publish src/LocalSendDotNet.App/LocalSendDotNet.App.csproj `
  -p:PublishProfile=win-x64-aot
```

For Windows on ARM64, use `win-arm64-aot` instead. Outputs are written under
`artifacts/publish/native-aot/<rid>/`.

The equivalent one-off command is:

```powershell
dotnet publish src/LocalSendDotNet.App/LocalSendDotNet.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 -p:NativeAot=true `
  -o artifacts/publish/native-aot/win-x64
```

The profiles enable `InvariantGlobalization`, as recommended for Reactor Native AOT.
Automatic Chinese/English selection reads Windows' language preferences through the
WinRT API instead of `CultureInfo`, so it continues to work without the managed
globalization tables. Reactor DevTools remains Debug-only and is therefore absent
from the trimmed retail binary.

## MSIX

The application uses single-project MSIX packaging, following ReactorGallery. The
default build still has `WindowsPackageType=None`; opt into packaging with:

```powershell
dotnet build src/LocalSendDotNet.App/LocalSendDotNet.App.csproj -c Release `
  -p:Platform=x64 `
  -p:LocalSendPackaged=true `
  -p:GenerateAppxPackageOnBuild=true
```

This produces an unsigned package under the app project's `AppPackages` directory.
It is suitable for validating the package layout, but Windows will not install it
until it is signed.

For a sideloadable build, set `Package.appxmanifest`'s `Identity Publisher` to the
exact subject of the signing certificate and add:

```powershell
-p:AppxPackageSigningEnabled=true `
-p:PackageCertificateKeyFile=C:\path\LocalSendDotNet.pfx
```

Keep the PFX and its password outside the repository. The manifest declares both
`internetClientServer` and `privateNetworkClientServer`, which the LocalSend HTTP
server and local-network discovery require, plus `runFullTrust` for the desktop app.

## Native AOT inside MSIX

Native AOT and MSIX are independent choices. This command publishes an unsigned
x64 MSIX whose application executable is Native AOT compiled:

```powershell
dotnet publish src/LocalSendDotNet.App/LocalSendDotNet.App.csproj -c Release `
  -r win-x64 -p:Platform=x64 `
  -p:LocalSendPackaged=true -p:NativeAot=true `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
```

The same signing properties described above produce an installable package. A
missing `mspdbcmf.exe` only prevents generation of the optional symbol package; it
does not prevent the `.msix` application package from being created.
