# Testing the New Release Pipeline

This document provides instructions for testing the new three-stage release pipeline.

## Overview

The release pipeline has been refactored into three independent stages:

1. **Build Artifacts** (`build-artifacts.yml`) - Builds unsigned artifacts
2. **Sign Artifacts** (`sign-artifacts.yml`) - Signs the artifacts
3. **Release** (`release.yml`) - Creates a public GitHub release

## Prerequisites for Testing

### Required GitHub Secrets

Before testing, ensure the following secrets are configured in the repository settings:

- `WINDOWS_CERT_PFX` - Base64-encoded Windows certificate
- `WINDOWS_CERT_PASS` - Certificate password
- `ANDROID_KEYSTORE` - Base64-encoded Android keystore
- `ANDROID_KEY_PASS` - Keystore password
- `ANDROID_KEY_ALIAS` - Key alias (e.g., "android")

> **Note:** The pipeline will fail at the signing stage if these secrets are not configured. This is expected behavior.

## Test Scenarios

### Test 1: Full Pipeline Run (Happy Path)

**Purpose:** Verify the complete workflow from build to release.

**Steps:**

1. **Trigger Build Artifacts**
   - Go to Actions → Build Artifacts → Run workflow
   - Input version: `0.1.0-test1`
   - Expected: Build completes and creates pre-release `build-0.1.0-test1`

2. **Trigger Sign Artifacts**
   - Go to Actions → Sign Artifacts → Run workflow
   - Input version: `0.1.0-test1`
   - Expected: Signs artifacts and creates pre-release `signed-0.1.0-test1`

3. **Trigger Release**
   - Go to Actions → Release → Run workflow
   - Input version: `0.1.0-test1`
   - Input notes: "Test release"
   - Expected: Creates public release `0.1.0-test1` and cleans up pre-releases

**Verification:**
- Check that three workflow runs completed successfully
- Verify the final release contains signed `.exe` and `.apk` files
- Verify intermediate releases were deleted

### Test 2: Idempotency (Prevent Duplicate Builds)

**Purpose:** Verify that stages skip when already completed.

**Steps:**

1. Run the full pipeline for version `0.1.0-test2`
2. Attempt to run Build Artifacts again with version `0.1.0-test2`
   - Expected: Build is skipped with message "Build artifacts already exist"
3. Attempt to run Sign Artifacts again with version `0.1.0-test2`
   - Expected: Signing is skipped with message "Signed artifacts already exist"
4. Attempt to run Release again with version `0.1.0-test2`
   - Expected: Release is skipped with message "Release already exists"

**Verification:**
- All re-runs complete quickly (within seconds)
- No new artifacts are created
- Skip messages appear in workflow logs

### Test 3: Fail-Fast Validation

**Purpose:** Verify that stages fail fast when prerequisites are missing.

**Steps:**

1. **Skip Build, Try to Sign**
   - Go to Actions → Sign Artifacts → Run workflow
   - Input version: `0.1.0-nonexistent`
   - Expected: Workflow fails immediately with "Build artifacts not found"

2. **Skip Build and Sign, Try to Release**
   - Go to Actions → Release → Run workflow
   - Input version: `0.1.0-nonexistent2`
   - Expected: Workflow fails immediately with "Signed artifacts not found"

**Verification:**
- Workflows fail within 1-2 minutes (no build attempted)
- Error messages clearly indicate the missing prerequisite

### Test 4: Retry After Signing Failure (Main Use Case)

**Purpose:** Verify that signing can be retried without rebuilding.

**Steps:**

1. **Build Artifacts**
   - Run Build Artifacts workflow with version `0.1.0-test3`
   - Expected: Build completes successfully

2. **Simulate Signing Failure**
   - Option A: Temporarily remove signing secrets and run Sign Artifacts
   - Option B: Let signing fail naturally if secrets are not configured
   - Expected: Signing fails

3. **Fix Configuration**
   - Restore or configure signing secrets correctly

4. **Retry Signing**
   - Run Sign Artifacts again with version `0.1.0-test3`
   - Expected: Signing completes successfully using existing build artifacts
   - **Key verification:** Build was not re-run

5. **Complete Release**
   - Run Release workflow with version `0.1.0-test3`
   - Expected: Release completes successfully

**Verification:**
- Total of 3 workflow runs (Build once, Sign twice, Release once)
- Build was only run once despite signing retry
- Final release contains correctly signed artifacts

### Test 5: GitVersion Integration

**Purpose:** Verify auto-versioning with GitVersion.

**Steps:**

1. **Trigger Build Without Version Input**
   - Go to Actions → Build Artifacts → Run workflow
   - Leave version input empty
   - Expected: GitVersion calculates version automatically (e.g., `0.2.3`)

2. **Complete Pipeline with Auto-Generated Version**
   - Note the version from Build workflow output
   - Run Sign and Release with that version
   - Expected: Full pipeline completes with auto-generated version

**Verification:**
- Version follows GitVersion semantic versioning rules
- Version is consistent across all three stages
- Git tag matches the auto-generated version

### Test 6: Git Tag Trigger

**Purpose:** Verify that pushing a git tag triggers the build.

**Steps:**

1. **Push a Git Tag**
   ```bash
   git tag 0.1.0-test-tag
   git push origin 0.1.0-test-tag
   ```
   - Expected: Build Artifacts workflow starts automatically

2. **Complete Pipeline**
   - Run Sign and Release manually with version `0.1.0-test-tag`
   - Expected: Full pipeline completes

**Verification:**
- Build was triggered automatically by tag push
- Signing and release still require manual trigger
- Final release is tagged with the pushed tag

## Expected Results Summary

| Test | Expected Outcome |
|------|-----------------|
| Test 1 | Full pipeline completes successfully |
| Test 2 | Re-runs skip with informative messages |
| Test 3 | Workflows fail fast with clear errors |
| Test 4 | Signing retry works without rebuild |
| Test 5 | GitVersion auto-generates correct version |
| Test 6 | Git tag triggers build automatically |

## Cleanup After Testing

After testing, clean up test releases and tags:

```bash
# Delete test releases
gh release delete 0.1.0-test1 --yes
gh release delete 0.1.0-test2 --yes
gh release delete 0.1.0-test3 --yes
gh release delete 0.1.0-test-tag --yes

# Delete test tags
git tag -d 0.1.0-test1
git tag -d 0.1.0-test2
git tag -d 0.1.0-test3
git tag -d 0.1.0-test-tag
git push origin --delete 0.1.0-test1
git push origin --delete 0.1.0-test2
git push origin --delete 0.1.0-test3
git push origin --delete 0.1.0-test-tag
```

## Troubleshooting

### Build fails with "MAUI workload not found"

This is a transient issue with the Windows runner. Retry the workflow.

### Sign fails with "signtool.exe not found"

The workflow should automatically find signtool. If this fails, check the Windows SDK installation on the runner.

### Release fails with "Permission denied"

Ensure the workflow has `contents: write` permission (it should by default).

### Artifacts not found despite successful build

Check that the `build-{version}` pre-release exists in the repository releases.

## Success Criteria

The pipeline is considered successful if:

1. ✅ All three stages can be run independently
2. ✅ Version coordination works correctly across stages
3. ✅ Idempotency prevents duplicate work
4. ✅ Fail-fast validation provides clear error messages
5. ✅ Signing can be retried without rebuilding
6. ✅ GitVersion integration works correctly
7. ✅ Git tag triggers build automatically
8. ✅ Final release contains signed artifacts
9. ✅ Intermediate pre-releases are cleaned up
10. ✅ Documentation is clear and comprehensive

## Notes

- The legacy single-stage pipeline (`release-legacy.yml`) remains available as a fallback
- The new pipeline requires more manual coordination but provides much better retry capabilities
- Consider creating a wrapper workflow in the future to automate all three stages
