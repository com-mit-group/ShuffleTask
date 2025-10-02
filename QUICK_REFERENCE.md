# Release Pipeline Quick Reference

Quick reference card for the three-stage release pipeline.

## 🚀 Quick Start

### Standard Release (3 Steps)

```
1. BUILD
   Actions → Build Artifacts → Run workflow
   Version: (leave empty or specify)
   ⏱️ ~10 minutes

2. SIGN
   Actions → Sign Artifacts → Run workflow
   Version: [same as build output]
   ⏱️ ~3 minutes

3. RELEASE
   Actions → Release → Run workflow
   Version: [same as previous]
   Notes: "Your release notes"
   ⏱️ ~2 minutes

✅ Total: ~15 minutes
```

### Retry After Signing Failure (2 Steps)

```
1. FIX
   Update signing configuration
   (e.g., rotate certificate)

2. SIGN (retry)
   Actions → Sign Artifacts → Run workflow
   Version: [same as build]
   ⏱️ ~3 minutes

✅ Time saved: ~10 minutes (no rebuild!)
```

## 📋 Workflow Reference

| Workflow | Trigger | Required Input | Optional Input | Duration | Output |
|----------|---------|----------------|----------------|----------|--------|
| **Build Artifacts** | Manual<br>Git Tag | `version` (optional) | - | ~10 min | `build-{version}` |
| **Sign Artifacts** | Manual | `version` | - | ~3 min | `signed-{version}` |
| **Release** | Manual | `version` | `notes` | ~2 min | `{version}` |

## 🔑 Version Coordination

All three workflows use the **same version string**:

```
Build outputs:    1.0.0
Sign uses:        1.0.0  ← must match
Release uses:     1.0.0  ← must match
```

### Version Sources
- **Manual Input** - Specify exact version (e.g., `1.0.0`)
- **GitVersion** - Auto-calculate from git history (default)
- **Git Tag** - Push tag triggers build (e.g., `git push origin 1.0.0`)

## 📦 Artifact Storage

| Stage | Release Name | Files | Visibility |
|-------|--------------|-------|------------|
| Build | `build-{version}` | `*-unsigned.exe`<br>`*-unsigned.apk`<br>`VERSION.txt` | Pre-release |
| Sign | `signed-{version}` | `*.exe` (signed)<br>`*.apk` (signed)<br>`VERSION.txt` | Pre-release |
| Release | `{version}` | `*.exe` (signed)<br>`*.apk` (signed) | **Public** |

## ⚙️ Required Secrets

| Secret | Purpose | Format |
|--------|---------|--------|
| `WINDOWS_CERT_PFX` | Windows Authenticode | Base64 |
| `WINDOWS_CERT_PASS` | Certificate password | Plain text |
| `ANDROID_KEYSTORE` | Android signing key | Base64 |
| `ANDROID_KEY_PASS` | Keystore password | Plain text |
| `ANDROID_KEY_ALIAS` | Key alias | Plain text |

### Encoding Secrets
```bash
# Encode certificate
base64 -i your-cert.pfx -o cert.txt

# Encode keystore
base64 -i your-keystore.jks -o keystore.txt
```

## ✅ Status Indicators

### Successful Run
```
✓ Build artifacts already exist for version 1.0.0
  Skipping build.
```

### Failed Run
```
✗ Build artifacts not found for version 1.0.0
  Please run the Build Artifacts workflow first.
```

## 🔄 Common Workflows

### New Release
```bash
1. Build (auto-version)
2. Sign (use build's version)
3. Release (use sign's version)
```

### Hotfix Release
```bash
1. Build (specify version: 1.0.1)
2. Sign (version: 1.0.1)
3. Release (version: 1.0.1)
```

### Retry Signing
```bash
1. (Build already complete)
2. Sign (retry with same version)
3. Release (continue as normal)
```

### Pre-release Testing
```bash
1. Build (version: 1.0.0-beta.1)
2. Sign (version: 1.0.0-beta.1)
3. Release (version: 1.0.0-beta.1)
```

## 🐛 Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| "Build artifacts not found" | Skipped build stage | Run Build first |
| "Signed artifacts not found" | Skipped sign stage | Run Sign first |
| "Already exists" | Stage already run | Expected, or delete release to re-run |
| "signtool.exe not found" | Windows SDK issue | Retry workflow (usually transient) |
| "MAUI workload not found" | Runner issue | Retry workflow |
| "Secrets missing" | Secrets not configured | Add secrets to repository settings |

## 📊 Performance Comparison

| Scenario | Legacy Pipeline | New Pipeline | Time Saved |
|----------|----------------|--------------|------------|
| **First release** | ~15 min | ~15 min | - |
| **Signing retry** | ~15 min (rebuild) | ~3 min (sign only) | **12 min** |
| **Multiple retries** | 30-45 min | 3-6 min | **85%+** |

## 🔗 Documentation Links

- **Full Documentation**: [RELEASE_PIPELINE.md](RELEASE_PIPELINE.md)
- **Testing Guide**: [TESTING_RELEASE_PIPELINE.md](TESTING_RELEASE_PIPELINE.md)
- **Workflow Diagram**: [WORKFLOW_DIAGRAM.md](WORKFLOW_DIAGRAM.md)
- **Implementation**: [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md)

## 📝 Cheat Sheet

### GitHub Actions URLs
```
Build:   https://github.com/yaron-E92/ShuffleTask/actions/workflows/build-artifacts.yml
Sign:    https://github.com/yaron-E92/ShuffleTask/actions/workflows/sign-artifacts.yml
Release: https://github.com/yaron-E92/ShuffleTask/actions/workflows/release.yml
```

### CLI Commands
```bash
# List releases
gh release list

# View specific release
gh release view build-1.0.0

# Download artifacts
gh release download signed-1.0.0

# Delete release
gh release delete build-1.0.0 --yes

# Create tag
git tag 1.0.0
git push origin 1.0.0

# Delete tag
git tag -d 1.0.0
git push origin --delete 1.0.0
```

## 🎯 Best Practices

1. ✅ **Use GitVersion** for regular releases (auto-versioning)
2. ✅ **Specify version** for hotfixes and testing
3. ✅ **Test locally** before running Sign if possible
4. ✅ **Check build logs** before proceeding to Sign
5. ✅ **Write clear release notes** for Release stage
6. ✅ **Keep intermediate releases** until final release succeeds
7. ✅ **Tag the commit** so you can rebuild later if needed

## 🚨 Important Notes

⚠️ **Version Coordination** - All three stages must use the exact same version string  
⚠️ **Idempotency** - Re-running a completed stage will skip with a message  
⚠️ **Prerequisites** - Each stage validates previous stages before proceeding  
⚠️ **Cleanup** - Release stage deletes intermediate `build-*` and `signed-*` releases  
⚠️ **Legacy Pipeline** - Old single-stage pipeline available as `release-legacy.yml`  

---

**Need help?** See the full documentation in [RELEASE_PIPELINE.md](RELEASE_PIPELINE.md)
