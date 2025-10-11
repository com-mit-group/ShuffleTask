# Background Notifications Testing Guide

This document explains how to manually test that auto-shuffle notifications fire reliably when the app is backgrounded.

## Prerequisites

Before testing, ensure the app has the necessary permissions:

### Android
1. Grant notification permission (POST_NOTIFICATIONS) when prompted on first launch
2. On Android 12+, the app will automatically request SCHEDULE_EXACT_ALARM permission
3. If needed, manually enable "Alarms & reminders" in app settings

### iOS/macOS
1. Grant notification permission when prompted on first launch
2. Ensure notifications are enabled in System Settings → Notifications → ShuffleTask

### Windows
1. Notifications should work by default
2. If not, enable notifications in Windows Settings → System → Notifications → ShuffleTask

## Manual Testing Steps

### Test 1: Background Notification Delivery

1. **Launch the app** and ensure auto-shuffle is enabled in Settings
2. **Add one or more active tasks** to your task list
3. **Perform a manual shuffle** to start a task timer
4. **Background the app** by switching to another app or locking the device
5. **Wait for the timer to expire** (you can set a short timer for testing, e.g., 1-2 minutes)
6. **Verify notification appears** even though the app is backgrounded

**Expected Result**: You should receive a notification when the timer expires, regardless of whether the app window is visible.

### Test 2: Auto-Shuffle Schedule

1. **Launch the app** and configure auto-shuffle settings:
   - Enable "Auto-shuffle" in Settings
   - Set reasonable gap times (e.g., min 5 minutes, max 15 minutes for testing)
   - Ensure at least one task has "Allow auto-shuffle" enabled
2. **Background the app** immediately after setup
3. **Wait for the scheduled auto-shuffle time** (check logs if available)
4. **Verify notification appears** for the auto-shuffled task

**Expected Result**: The app should automatically select and notify about a new task based on the gap schedule, even when backgrounded.

### Test 3: Quiet Hours Respect

1. **Configure quiet hours** in Settings (e.g., 10 PM - 7 AM)
2. **Start a task** with a timer that would expire during quiet hours
3. **Background the app**
4. **Verify notification is delayed** until quiet hours end

**Expected Result**: Notifications should respect quiet hours and be rescheduled appropriately.

## Platform-Specific Behaviors

### Android
- Uses `AlarmManager.SetExactAndAllowWhileIdle` for precise background delivery
- May be affected by battery optimization settings
- On some devices, you may need to disable battery optimization for ShuffleTask:
  - Settings → Apps → ShuffleTask → Battery → Unrestricted

### iOS/macOS
- Uses `UNUserNotificationCenter` which queues notifications for background delivery
- Notifications are delivered reliably by iOS/macOS notification system
- Low Power Mode may delay some notifications

### Windows
- Uses `ScheduledToastNotification` for background delivery
- Works reliably when notifications are enabled in Windows Settings
- Focus Assist mode may suppress notifications

### Linux
- Limited background notification support
- Falls back to in-app alerts when app is active
- Background notifications require the app process to remain running

## Troubleshooting

If notifications don't appear when the app is backgrounded:

1. **Check notification permissions** in system settings
2. **Verify auto-shuffle is enabled** in app Settings
3. **Check battery optimization** settings (Android)
4. **Review quiet hours** configuration
5. **Ensure tasks have "Allow auto-shuffle" enabled**
6. **Check system Focus/Do Not Disturb** settings

## Technical Implementation

The implementation ensures background notifications by:

1. **Not pausing ShuffleCoordinatorService** when the app backgrounds
2. **Using OS-native scheduled notification APIs** that support background delivery
3. **Requesting appropriate permissions** for background notification delivery
4. **Respecting OS power-saving restrictions** and user preferences

For more details, see:
- `ARCHITECTURE.md` - Notification Pipeline section
- `ShuffleTask.Presentation/Platforms/*/Services/NotificationService.*.cs` - Platform-specific implementations
