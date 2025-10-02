# Release Pipeline Documentation

## Overview

The ShuffleTask release pipeline is split into three independent stages to enable efficient retries without unnecessary rebuilds:

1. **Build Artifacts** - Compiles and produces unsigned artifacts
2. **Sign Artifacts** - Signs the previously built artifacts
3. **Release** - Creates a public GitHub release with signed artifacts

All three stages are coordinated by a **shared semantic version** that ensures the correct artifacts are retrieved at each stage.

## Workflow Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  Build Artifacts (build-artifacts.yml)                         │
│  ├─ Determine version (manual or GitVersion)                   │
│  ├─ Check if build already exists                              │
│  ├─ Build Windows .exe and Android .apk                        │
│  ├─ Normalize artifact names (ShuffleTask-{version}-unsigned)  │
│  └─ Upload to GitHub release "build-{version}"                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  Sign Artifacts (sign-artifacts.yml)                           │
│  ├─ Accept version as input parameter                          │
│  ├─ Check if signing already completed                         │
│  ├─ Download artifacts from "build-{version}"                  │
│  ├─ Validate signing prerequisites                             │
│  ├─ Sign Windows .exe with Authenticode                        │
│  ├─ Sign Android .apk with apksigner                           │
│  ├─ Rename artifacts (ShuffleTask-{version}.exe/.apk)          │
│  └─ Upload to GitHub release "signed-{version}"                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  Release (release.yml)                                         │
│  ├─ Accept version and release notes as inputs                 │
│  ├─ Check if release already exists                            │
│  ├─ Download signed artifacts from "signed-{version}"          │
│  ├─ Create release notes with installation instructions        │
│  ├─ Tag repository with "v{version}"                           │
│  ├─ Create public GitHub release "{version}"                   │
│  └─ Clean up intermediate "build-*" and "signed-*" releases    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Key Features

### Version Coordination
- All three pipelines use the **same semantic version** as the source of truth
- Version can be specified manually or auto-generated using GitVersion
- Artifacts are named with the version: `ShuffleTask-1.0.0.exe`, `ShuffleTask-1.0.0.apk`

### Idempotency
- Each stage checks if it has already been completed for the given version
- If artifacts already exist, the stage is skipped with an informative message
- Prevents accidental rebuilds or re-signing

### Fail-Fast Validation
- Sign pipeline validates that build artifacts exist before proceeding
- Release pipeline validates that signed artifacts exist before proceeding
- Clear error messages guide users to run the required prerequisite stage

### Immutable Artifacts
- Build artifacts are stored in a pre-release `build-{version}`
- Signed artifacts are stored in a pre-release `signed-{version}`
- Final release `{version}` is public and contains only signed artifacts
- Intermediate releases are automatically cleaned up after final release

## Usage

### Manual Release Workflow

#### 1. Build Artifacts

Trigger the **Build Artifacts** workflow manually:

```yaml
Workflow: Build Artifacts
Inputs:
  version: "1.0.0"  # Optional: Leave empty to auto-generate
```

This will:
- Build Windows and Android artifacts
- Upload unsigned artifacts to release `build-1.0.0`
- Output the version number for the next stage

**Result:** Unsigned artifacts available in pre-release `build-1.0.0`

#### 2. Sign Artifacts

After the build completes, trigger the **Sign Artifacts** workflow:

```yaml
Workflow: Sign Artifacts
Inputs:
  version: "1.0.0"  # Must match the version from build
```

This will:
- Download unsigned artifacts from `build-1.0.0`
- Sign both Windows and Android artifacts
- Upload signed artifacts to release `signed-1.0.0`

**Result:** Signed artifacts available in pre-release `signed-1.0.0`

> ⚠️ **Note:** If signing fails, you can simply re-run the Sign Artifacts workflow without rebuilding!

#### 3. Create Release

After signing completes, trigger the **Release** workflow:

```yaml
Workflow: Release
Inputs:
  version: "1.0.0"  # Must match the version from previous stages
  notes: "Bug fixes and performance improvements"  # Optional
```

This will:
- Download signed artifacts from `signed-1.0.0`
- Create a git tag `v1.0.0`
- Create public GitHub release `1.0.0` with signed artifacts
- Clean up intermediate `build-1.0.0` and `signed-1.0.0` releases

**Result:** Public release `1.0.0` available for download

### Automated Release via Git Tag

You can also trigger the build automatically by pushing a git tag:

```bash
git tag 1.0.0
git push origin 1.0.0
```

This will:
- Automatically trigger the **Build Artifacts** workflow
- You still need to manually trigger **Sign Artifacts** and **Release**

### Retry Scenarios

#### Signing Failed - Need to Retry

If the signing stage fails (e.g., certificate issue, timeout):

1. Fix the signing configuration (update secrets, etc.)
2. Re-run the **Sign Artifacts** workflow with the same version
3. No rebuild required! The build artifacts are reused.

#### Build Succeeded, Want to Test Signing Locally First

1. Run the **Build Artifacts** workflow
2. Download artifacts from `build-{version}` release
3. Test signing locally
4. Once satisfied, run **Sign Artifacts** workflow

#### Need to Rebuild Due to Code Changes

1. Delete the `build-{version}` release (if it exists)
2. Make your code changes
3. Re-run the **Build Artifacts** workflow
4. Proceed with **Sign Artifacts** and **Release** as normal

## Version Management

### Auto-Versioning with GitVersion

When no version is specified, GitVersion automatically calculates the version based on:
- Git tags
- Branch name
- Commit history

This is configured in `.github/GitVersion.yaml`:

```yaml
mode: ContinuousDeployment
commit-message-incrementing: MergeMessageOnly
```

### Manual Versioning

You can override GitVersion by providing a version in the workflow input:

```
version: "2.1.0"
```

This is useful for:
- Hotfix releases
- Pre-release versions (e.g., "2.1.0-beta.1")
- Testing the pipeline

## Artifact Storage

Artifacts are stored as GitHub releases:

| Release Tag | Type | Purpose | Visibility |
|-------------|------|---------|------------|
| `build-{version}` | Pre-release | Stores unsigned build artifacts | Internal |
| `signed-{version}` | Pre-release | Stores signed artifacts | Internal |
| `{version}` | Release | Public release with signed artifacts | Public |
| `v{version}` | Git Tag | Marks the commit for the release | Public |

## Required Secrets

The following repository secrets must be configured for signing:

### Windows Signing
- `WINDOWS_CERT_PFX` - Base64-encoded Authenticode certificate (.pfx file)
- `WINDOWS_CERT_PASS` - Password for the certificate

### Android Signing
- `ANDROID_KEYSTORE` - Base64-encoded Android keystore (.jks file)
- `ANDROID_KEY_PASS` - Keystore password
- `ANDROID_KEY_ALIAS` - Key alias (default: "android")

### Encoding Certificates

To encode your certificates to base64:

```bash
# Windows certificate
base64 -i your-cert.pfx -o cert-base64.txt

# Android keystore
base64 -i your-keystore.jks -o keystore-base64.txt
```

Then add the contents to GitHub Secrets.

## Troubleshooting

### "Build artifacts not found"

**Error:** Sign Artifacts workflow fails with "Build artifacts not found"

**Solution:** Run the **Build Artifacts** workflow first with the same version.

### "Signed artifacts not found"

**Error:** Release workflow fails with "Signed artifacts not found"

**Solution:** Run the **Sign Artifacts** workflow first with the same version.

### "Release already exists"

**Error:** Release workflow skips with "Release already exists"

**Solution:** This is expected behavior. If you need to re-release:
1. Delete the existing release `{version}`
2. Re-run the **Release** workflow

### "signtool.exe not found"

**Error:** Sign Artifacts fails to find signtool

**Solution:** The workflow automatically searches for signtool in Windows SDK. If this fails, the Windows runner may need SDK reinstallation (rare).

### Signing validation failures

**Error:** Android signing fails with "Verification failed"

**Solution:** Check that:
- Keystore file is valid
- Password is correct
- Key alias exists in the keystore

## Pipeline Comparison

### Old Pipeline (release-legacy.yml)

**Pros:**
- Single workflow, simple to trigger

**Cons:**
- Must rebuild everything if signing fails
- Long build times for retries
- Wastes CI resources

### New Pipeline (build → sign → release)

**Pros:**
- Can retry signing without rebuild
- Faster iteration when debugging signing issues
- Immutable build artifacts
- Clear separation of concerns
- Better traceability (each stage is a separate run)

**Cons:**
- Requires manual coordination of three workflows
- More complex setup

## Migration from Legacy Pipeline

The legacy single-stage pipeline is preserved as `release-legacy.yml` for backward compatibility.

To migrate to the new pipeline:

1. Use the new **Build Artifacts** workflow instead of **Manual Release**
2. Follow with **Sign Artifacts** and **Release** workflows
3. Update any automation scripts or documentation to reference the new workflows

## Best Practices

1. **Use auto-versioning for regular releases** - Let GitVersion handle versioning based on git history
2. **Use manual versioning for hotfixes** - Specify exact version numbers for emergency releases
3. **Test signing locally first** - Download build artifacts and test signing before running the pipeline
4. **Keep intermediate releases** - Don't manually delete `build-*` and `signed-*` releases until after final release
5. **Document release notes** - Always provide meaningful release notes in the Release workflow

## Examples

### Example 1: Standard Release

```bash
# 1. Trigger Build Artifacts (leave version empty for auto-generation)
# GitHub Actions → Build Artifacts → Run workflow
# Inputs: (leave version empty)

# Build completes, outputs version: 1.2.3

# 2. Trigger Sign Artifacts
# GitHub Actions → Sign Artifacts → Run workflow
# Inputs:
#   version: 1.2.3

# Signing completes

# 3. Trigger Release
# GitHub Actions → Release → Run workflow
# Inputs:
#   version: 1.2.3
#   notes: "New features:\n- Feature A\n- Feature B"

# Release is live!
```

### Example 2: Hotfix Release

```bash
# 1. Build with specific version
# GitHub Actions → Build Artifacts → Run workflow
# Inputs:
#   version: 1.2.4

# 2. Sign
# GitHub Actions → Sign Artifacts → Run workflow
# Inputs:
#   version: 1.2.4

# 3. Release
# GitHub Actions → Release → Run workflow
# Inputs:
#   version: 1.2.4
#   notes: "Hotfix: Critical bug fix"
```

### Example 3: Retry After Signing Failure

```bash
# 1. Build completes successfully (version 1.3.0)

# 2. First signing attempt fails
# GitHub Actions → Sign Artifacts → Run workflow
# Inputs:
#   version: 1.3.0
# Result: FAILED (certificate issue)

# Fix the certificate secret

# 3. Retry signing (no rebuild!)
# GitHub Actions → Sign Artifacts → Run workflow
# Inputs:
#   version: 1.3.0
# Result: SUCCESS (reuses build artifacts)

# 4. Release
# GitHub Actions → Release → Run workflow
# Inputs:
#   version: 1.3.0
#   notes: "Release 1.3.0"
```

## FAQ

**Q: Can I run all three stages automatically?**
A: Currently, each stage must be triggered manually. You could create a wrapper workflow that calls all three, but manual triggers provide more control and visibility.

**Q: What happens if I try to release version 1.0.0 twice?**
A: The Release workflow will detect the existing release and skip creation with an informative message.

**Q: Can I keep the intermediate releases for debugging?**
A: Yes! Comment out or remove the "Clean up pre-release artifacts" step in the Release workflow.

**Q: How do I roll back a release?**
A: Delete the public release from GitHub, then re-run the Release workflow with the same version (assuming signed artifacts still exist).

**Q: Can I use this pipeline for pre-release versions?**
A: Yes! Use version strings like "1.0.0-beta.1" or "1.0.0-rc.1".

## Support

For issues or questions about the release pipeline:
1. Check the workflow run logs in GitHub Actions
2. Review this documentation
3. Open an issue in the repository
