# Release Pipeline Workflow Diagram

This diagram illustrates the flow of the new three-stage release pipeline.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                        STAGE 1: BUILD ARTIFACTS                          │
│                      (build-artifacts.yml)                               │
└──────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    │                               │
            Manual Trigger                   Git Tag Push
           (workflow_dispatch)                (tags: X.Y.Z)
                    │                               │
                    └───────────────┬───────────────┘
                                    │
                    ┌───────────────▼───────────────┐
                    │ Determine Version             │
                    │ • Manual input OR             │
                    │ • Git tag OR                  │
                    │ • GitVersion auto-generate    │
                    └───────────────┬───────────────┘
                                    │
                    ┌───────────────▼───────────────┐
                    │ Check if build exists         │
                    │ (look for build-{version})    │
                    └───────────────┬───────────────┘
                                    │
                    ┌───────────────▼───────────────┐
                    │   Already exists?             │
                    │                               │
                    │   YES: Skip ───────────────┐  │
                    │   NO: Continue             │  │
                    └────────────────┬───────────┘  │
                                     │              │
                    ┌────────────────▼───────────┐  │
                    │ Build Windows .exe         │  │
                    │ Build Android .apk         │  │
                    └────────────────┬───────────┘  │
                                     │              │
                    ┌────────────────▼───────────┐  │
                    │ Rename artifacts:          │  │
                    │ • ShuffleTask-{ver}-       │  │
                    │   unsigned.exe             │  │
                    │ • ShuffleTask-{ver}-       │  │
                    │   unsigned.apk             │  │
                    └────────────────┬───────────┘  │
                                     │              │
                    ┌────────────────▼───────────┐  │
                    │ Upload to GitHub release:  │  │
                    │ build-{version}            │◄─┘
                    │ (pre-release, internal)    │
                    └────────────────┬───────────┘
                                     │
                                     ▼
            ╔════════════════════════════════════════════╗
            ║  Build artifacts available                 ║
            ║  Release: build-{version}                  ║
            ║  Files:                                    ║
            ║  • ShuffleTask-{version}-unsigned.exe      ║
            ║  • ShuffleTask-{version}-unsigned.apk      ║
            ║  • VERSION.txt                             ║
            ╚════════════════════════════════════════════╝
                                     │
                                     │
┌────────────────────────────────────┼────────────────────────────────────┐
│                        STAGE 2: SIGN ARTIFACTS                           │
│                       (sign-artifacts.yml)                               │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │   Manual Trigger               │
                    │   (workflow_dispatch)          │
                    │   Input: version               │
                    └────────────────┬───────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │ Check if signing completed     │
                    │ (look for signed-{version})    │
                    └────────────────┬───────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │   Already signed?              │
                    │                                │
                    │   YES: Skip ────────────────┐  │
                    │   NO: Continue              │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Check if build exists       │  │
                    │ (look for build-{version})  │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │   Build exists?             │  │
                    │                             │  │
                    │   NO: FAIL FAST             │  │
                    │   YES: Continue             │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Download unsigned artifacts │  │
                    │ from build-{version}        │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Validate signing secrets:   │  │
                    │ • WINDOWS_CERT_PFX          │  │
                    │ • WINDOWS_CERT_PASS         │  │
                    │ • ANDROID_KEYSTORE          │  │
                    │ • ANDROID_KEY_PASS          │  │
                    └────────────────┬────────────┘  │
                                     │               │
                ┌────────────────────▼────────────┐  │
                │ RETRY POINT: If signing fails,  │  │
                │ you can re-run from here without│  │
                │ rebuilding the artifacts        │  │
                └────────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Sign Windows .exe           │  │
                    │ (Authenticode signature)    │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Sign Android .apk           │  │
                    │ (zipalign + apksigner)      │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Rename signed artifacts:    │  │
                    │ • ShuffleTask-{ver}.exe     │  │
                    │ • ShuffleTask-{ver}.apk     │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Upload to GitHub release:   │  │
                    │ signed-{version}            │◄─┘
                    │ (pre-release, internal)     │
                    └────────────────┬────────────┘
                                     │
                                     ▼
            ╔════════════════════════════════════════════╗
            ║  Signed artifacts available                ║
            ║  Release: signed-{version}                 ║
            ║  Files:                                    ║
            ║  • ShuffleTask-{version}.exe (signed)      ║
            ║  • ShuffleTask-{version}.apk (signed)      ║
            ║  • VERSION.txt                             ║
            ╚════════════════════════════════════════════╝
                                     │
                                     │
┌────────────────────────────────────┼────────────────────────────────────┐
│                        STAGE 3: RELEASE                                  │
│                         (release.yml)                                    │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │   Manual Trigger               │
                    │   (workflow_dispatch)          │
                    │   Input: version, notes        │
                    └────────────────┬───────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │ Check if release exists        │
                    │ (look for {version})           │
                    └────────────────┬───────────────┘
                                     │
                    ┌────────────────▼───────────────┐
                    │   Already released?            │
                    │                                │
                    │   YES: Skip ────────────────┐  │
                    │   NO: Continue              │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Check if signed exists      │  │
                    │ (look for signed-{version}) │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │   Signed exists?            │  │
                    │                             │  │
                    │   NO: FAIL FAST             │  │
                    │   YES: Continue             │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Download signed artifacts   │  │
                    │ from signed-{version}       │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Generate release notes      │  │
                    │ with installation guide     │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Create git tag:             │  │
                    │ v{version}                  │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Create public GitHub        │  │
                    │ release: {version}          │  │
                    └────────────────┬────────────┘  │
                                     │               │
                    ┌────────────────▼────────────┐  │
                    │ Clean up intermediate       │  │
                    │ releases:                   │  │
                    │ • Delete build-{version}    │  │
                    │ • Delete signed-{version}   │◄─┘
                    └────────────────┬────────────┘
                                     │
                                     ▼
            ╔════════════════════════════════════════════╗
            ║  PUBLIC RELEASE AVAILABLE                  ║
            ║  Release: {version}                        ║
            ║  Git Tag: v{version}                       ║
            ║  Files:                                    ║
            ║  • ShuffleTask-{version}.exe (signed)      ║
            ║  • ShuffleTask-{version}.apk (signed)      ║
            ║                                            ║
            ║  Users can now download the release!       ║
            ╚════════════════════════════════════════════╝


═══════════════════════════════════════════════════════════════════════════
                            KEY BENEFITS
═══════════════════════════════════════════════════════════════════════════

✅ IMMUTABLE BUILDS
   Build artifacts are created once and reused throughout the pipeline.
   No risk of code changes between stages.

✅ RETRY WITHOUT REBUILD
   If signing fails, simply re-run Stage 2.
   No need to rebuild from scratch, saving significant time.

✅ INDEPENDENT STAGES
   Each stage can be run independently and tested separately.
   Clear separation of concerns.

✅ VERSION COORDINATION
   Semantic version is the single source of truth.
   All stages use the same version to find artifacts.

✅ IDEMPOTENCY
   Re-running a stage that already succeeded skips with a message.
   Prevents accidental duplicate work.

✅ FAIL-FAST VALIDATION
   Each stage validates prerequisites before starting work.
   Clear error messages guide users to run the correct stage.

✅ AUDIT TRAIL
   Each stage creates a separate GitHub Actions run.
   Easy to track which stage succeeded or failed.

✅ STORAGE EFFICIENCY
   Intermediate pre-releases are automatically cleaned up.
   Only the final public release remains.


═══════════════════════════════════════════════════════════════════════════
                        TYPICAL WORKFLOW
═══════════════════════════════════════════════════════════════════════════

🔨 STAGE 1: Build (5-10 minutes)
   GitHub Actions → Build Artifacts → Run workflow
   Input version: 1.0.0 (or leave empty for auto-version)
   
   ↓ Wait for build to complete
   ↓ Check that build-1.0.0 release was created

✍️  STAGE 2: Sign (2-3 minutes)
   GitHub Actions → Sign Artifacts → Run workflow
   Input version: 1.0.0
   
   ↓ Wait for signing to complete
   ↓ Check that signed-1.0.0 release was created
   
   ⚠️  If signing fails:
       • Fix signing configuration
       • Re-run Stage 2 (no rebuild!)

🚀 STAGE 3: Release (1-2 minutes)
   GitHub Actions → Release → Run workflow
   Input version: 1.0.0
   Input notes: "Bug fixes and improvements"
   
   ↓ Wait for release to complete
   ↓ Check that public release 1.0.0 is available
   
   ✅ Release is live!


═══════════════════════════════════════════════════════════════════════════
                        ARTIFACT LIFECYCLE
═══════════════════════════════════════════════════════════════════════════

Time →

    Build Stage              Sign Stage            Release Stage
        │                        │                      │
        ├─ Create                │                      │
        │  build-1.0.0           │                      │
        │  (pre-release)         │                      │
        │                        │                      │
        └────────────────────────┤                      │
                                 ├─ Create              │
                                 │  signed-1.0.0        │
                                 │  (pre-release)       │
                                 │                      │
                                 └──────────────────────┤
                                                        ├─ Create
                                                        │  1.0.0
                                                        │  (public)
                                                        │
                                                        ├─ Delete
                                                        │  build-1.0.0
                                                        │
                                                        └─ Delete
                                                           signed-1.0.0

Final State: Only public release 1.0.0 remains


═══════════════════════════════════════════════════════════════════════════
                     COMPARISON WITH LEGACY PIPELINE
═══════════════════════════════════════════════════════════════════════════

LEGACY (release-legacy.yml):
┌─────────────────────────────────────────┐
│  Single Job: Build + Sign + Release     │
│  (~15-20 minutes)                       │
└─────────────────────────────────────────┘
   ↓
   If signing fails → Must rebuild everything → Waste 10+ minutes

NEW PIPELINE:
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  Build       │ →  │  Sign        │ →  │  Release     │
│  (10 min)    │    │  (3 min)     │    │  (2 min)     │
└──────────────┘    └──────────────┘    └──────────────┘
                        ↓
                        If signing fails → Retry sign only → Save 10+ minutes!


═══════════════════════════════════════════════════════════════════════════
```

For detailed usage instructions, see [RELEASE_PIPELINE.md](RELEASE_PIPELINE.md).
For testing scenarios, see [TESTING_RELEASE_PIPELINE.md](TESTING_RELEASE_PIPELINE.md).
