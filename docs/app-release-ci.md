# Application GitHub Release CI

The `App GitHub Release` workflow creates these Windows assets for x64 and ARM64:

- a self-contained Native AOT portable ZIP;
- a separate native-symbols ZIP;
- a signed managed MSIX sideload ZIP (`LocalSendDotNet-<version>-<platform>.zip`);
- a signed Native AOT MSIX sideload ZIP (`LocalSendDotNet-<version>-<platform>-aot.zip`).

Each MSIX ZIP is the standard `*_Test` AppPackages folder: `Install.ps1`,
`Add-AppDevPackage.ps1`, the `.msix`, and the matching `.cer`. Unzip it and run
`Install.ps1`.

The workflow runs for tags in `app-vMAJOR.MINOR.PATCH` or
`app-vMAJOR.MINOR.PATCH.REVISION` form and can also be started manually. The `app-`
prefix keeps application releases separate from the `v*` tags used to publish the
Core NuGet package. GitHub Releases are marked as prereleases while the application
is experimental.

## Required repository secrets

Create the following Actions secrets in the GitHub repository:

| Secret | Value |
| --- | --- |
| `MSIX_CERTIFICATE_BASE64` | Base64 encoding of the PFX containing the self-signed certificate and private key |
| `MSIX_CERTIFICATE_PASSWORD` | Password used when the PFX was exported |

Encode the PFX and copy the result without printing it to the terminal:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\LocalSendDotNet.pfx")) |
    Set-Clipboard
```

The certificate subject must exactly match the `Publisher` in
`src/LocalSendDotNet.App/Package.appxmanifest`. Never commit a PFX, its password, or
its Base64 representation. The workflow imports it into the temporary runner's
Current User certificate store, removes the temporary PFX immediately, and removes
the imported certificate after packaging.

## Publishing

After the workflow and Secrets are present on the default branch, publish with:

```powershell
git tag -a app-v0.1.0 -m "Application release app-v0.1.0"
git push origin app-v0.1.0
```

The tag is converted to the four-part MSIX version (`app-v0.1.0` becomes
`0.1.0.0`). Prerelease suffixes such as `app-v0.1.0-preview.1` are intentionally
rejected because MSIX versions contain four numeric components.

The private key never becomes a release artifact.
