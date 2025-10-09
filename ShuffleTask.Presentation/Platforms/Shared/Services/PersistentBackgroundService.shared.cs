#if !ANDROID && !IOS && !MACCATALYST
using System;

namespace ShuffleTask.Presentation.Services;

internal partial class PersistentBackgroundService
{
    partial void InitializePlatform(TimeProvider clock, ref IPersistentBackgroundPlatform? platform)
    {
        // No platform-specific persistent background scheduling is available on this target.
        // The default no-op platform is retained.
    }
}
#endif
