# Development MSIX installation

GitHub Releases ship a standard AppPackages sideload ZIP for each architecture
(`LocalSendDotNet-<version>-x64.zip` or `...-ARM64.zip`, plus `-aot` variants).
The ZIP contains `Install.ps1`, the `.msix`, and the matching certificate.

1. Download the ZIP that matches the computer: `x64` for Intel/AMD PCs, `ARM64`
   for Windows on Arm. Use the `-aot` ZIP only when you want the Native AOT
   build.
2. Extract the archive.
3. In the extracted `*_Test` folder, run `Install.ps1` (or
   `Add-AppDevPackage.ps1`). The script installs the certificate if needed and
   then installs the package.

Only install packages downloaded directly from the
`kusutori/LocalSendDotNet` GitHub repository. Remove the development certificate
from the Local Machine **Trusted People** store when these builds are no longer
needed.

Future Microsoft Store packages use Microsoft's Store signature and do not
require this sideload script.
