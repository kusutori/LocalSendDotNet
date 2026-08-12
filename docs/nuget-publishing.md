# Publishing LocalSendDotNet.Core to nuget.org

`LocalSendDotNet.Core` is packed and tested by the normal CI workflow. The
`publish-nuget` workflow adds a release gate: a manual run only produces a
downloadable GitHub Actions artifact, while a version tag publishes to nuget.org.

## One-time trusted publishing setup

No long-lived API key is required. Nuget.org validates a GitHub OIDC token and
returns a single-use, short-lived API key to the workflow immediately before push.

On nuget.org, sign in and open **Trusted Publishing**, then add a GitHub policy
owned by the nuget.org user or organization that will own this package:

| Field | Value |
| --- | --- |
| Repository owner | `kusutori` |
| Repository | `LocalSendDotNet` |
| Workflow file | `publish-nuget.yml` |
| Environment | `release` |

Enter the workflow file name only, without `.github/workflows/`. Names are
case-insensitive, but using the spelling above makes audits easier.

In GitHub:

1. Open **Settings → Environments** and create the `release` environment. Optional
   protection rules can require approval before the publishing job starts.
2. Open **Settings → Secrets and variables → Actions** and create `NUGET_USER`.
   Its value is the nuget.org profile username that owns the trusted publishing
   policy—not an email address and not a key.

The workflow permission `id-token: write` lets GitHub issue the OIDC token. The
official `NuGet/login` action exchanges it for a temporary API key; the repository
never stores a reusable publishing credential.

## First release

The package version is the `<Version>` value in
`src/LocalSendDotNet.Core/LocalSendDotNet.Core.csproj`. Before publishing:

1. Confirm `CHANGELOG.md` contains that version.
2. Push the release commit and wait for the normal `ci` workflow to pass.
3. Run `publish-nuget` manually once and inspect the downloaded `.nupkg` and
   `.snupkg` artifacts.
4. Create and push a matching tag. For version `0.2.0-preview.2`:

   ```powershell
   git tag -a v0.2.0-preview.2 -m "LocalSendDotNet.Core 0.2.0-preview.2"
   git push origin v0.2.0-preview.2
   ```

The workflow rejects a tag whose name does not exactly equal `v` plus the project
version. Nuget.org package versions are immutable: after publication, fixes require
a new version and tag.

## Troubleshooting the first publish

New policies can initially be pending for seven days. The first successful publish
provides GitHub's immutable repository and owner IDs and permanently activates the
policy. If the first release is delayed past that window, restart the activation
window on nuget.org before pushing the tag.
