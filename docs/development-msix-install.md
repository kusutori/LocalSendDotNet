# Development MSIX installation

The MSIX files in GitHub Releases are signed with the project's self-signed
`CN=kusut` certificate. Windows does not trust this certificate by default.
Future Microsoft Store packages use Microsoft's Store signature and do not require
these steps.

## Verify and trust the certificate

1. Download `LocalSendDotNet-Development.cer` from the same GitHub Release as the
   MSIX.
2. Open the certificate and confirm that **Issued to** and **Issued by** are both
   `kusut`.
3. On the **Details** tab, confirm that the SHA-1 thumbprint is
   `03A206B72E0E34C7FFA1528F27EBFF97C2BAAC59`.
4. Select **Install Certificate**, choose **Local Machine**, then place it in
   **Trusted People**. Administrator approval is required.
5. Install the MSIX matching the computer architecture: `x64` for normal Intel/AMD
   Windows PCs or `ARM64` for Windows on Arm.

Only trust the certificate when the files were downloaded directly from the
`kusutori/LocalSendDotNet` GitHub repository. Remove it from the Local Machine
**Trusted People** certificate store when development builds are no longer needed.

This development certificate is valid through August 12, 2028. A future certificate
rotation requires updating this document and trusting the replacement certificate
before installing later releases.
