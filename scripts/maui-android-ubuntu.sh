#!/usr/bin/env bash
set -euo pipefail

PROJECT="ShuffleTask.Presentation/ShuffleTask.Presentation.csproj"
FRAMEWORK="net10.0-android"
CONFIGURATION="Debug"
MODE="build"
DEVICE_SERIAL=""

usage() {
  cat <<'USAGE'
Usage: bash scripts/maui-android-ubuntu.sh [build|run] [--configuration Debug|Release] [--device SERIAL]

Build or run the MAUI host from Ubuntu through the Android target.

Examples:
  bash scripts/maui-android-ubuntu.sh build
  bash scripts/maui-android-ubuntu.sh run
  bash scripts/maui-android-ubuntu.sh run --device emulator-5554
USAGE
}

fail() {
  printf 'ERROR: %s\n' "$1" >&2
  exit 1
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    fail "$2"
  fi
}

resolve_android_sdk() {
  if [[ -n "${ANDROID_HOME:-}" ]]; then
    printf '%s\n' "$ANDROID_HOME"
    return 0
  fi

  if [[ -n "${ANDROID_SDK_ROOT:-}" ]]; then
    printf '%s\n' "$ANDROID_SDK_ROOT"
    return 0
  fi

  if [[ -d "$HOME/Android/Sdk" ]]; then
    printf '%s\n' "$HOME/Android/Sdk"
    return 0
  fi

  return 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    build|run)
      MODE="$1"
      shift
      ;;
    --configuration)
      [[ $# -ge 2 ]] || fail "--configuration requires Debug or Release."
      CONFIGURATION="$2"
      shift 2
      ;;
    --device)
      [[ $# -ge 2 ]] || fail "--device requires an Android device serial."
      DEVICE_SERIAL="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      usage >&2
      fail "Unknown argument: $1"
      ;;
  esac
done

case "$CONFIGURATION" in
  Debug|Release) ;;
  *) fail "--configuration must be Debug or Release." ;;
esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

[[ -f "$PROJECT" ]] || fail "Cannot find $PROJECT. Run this script from the ShuffleTask repository."

require_command dotnet "Install the .NET SDK that matches the repo target, then rerun this command."

if ! dotnet workload list 2>/dev/null | grep -Eq '(^|[[:space:]])maui($|[[:space:]]|-)|(^|[[:space:]])maui-android($|[[:space:]])'; then
  fail "Install the .NET MAUI workload with: dotnet workload install maui"
fi

ANDROID_SDK="$(resolve_android_sdk)" || fail "Install the Android SDK and set ANDROID_HOME or ANDROID_SDK_ROOT, or use the default $HOME/Android/Sdk path."
[[ -d "$ANDROID_SDK" ]] || fail "Android SDK path does not exist: $ANDROID_SDK"
[[ -d "$ANDROID_SDK/platforms" ]] || fail "Android SDK platforms are missing. Install an Android platform with sdkmanager, for example: sdkmanager \"platforms;android-35\""
[[ -d "$ANDROID_SDK/platform-tools" ]] || fail "Android SDK platform-tools are missing. Install them with: sdkmanager \"platform-tools\""

BUILD_ARGS=(
  "$PROJECT"
  -f "$FRAMEWORK"
  -c "$CONFIGURATION"
)

if [[ "$MODE" == "run" ]]; then
  ADB="$ANDROID_SDK/platform-tools/adb"
  [[ -x "$ADB" ]] || ADB="$(command -v adb || true)"
  [[ -n "$ADB" ]] || fail "Cannot find adb. Install Android platform-tools or add adb to PATH."

  if [[ -z "$DEVICE_SERIAL" ]]; then
    mapfile -t DEVICES < <("$ADB" devices | awk 'NR > 1 && $2 == "device" { print $1 }')
    [[ "${#DEVICES[@]}" -gt 0 ]] || fail "No attached Android device or running emulator found. Start an emulator or connect a device, then rerun."
  else
    if ! "$ADB" devices | awk 'NR > 1 && $1 == serial && $2 == "device" { found = 1 } END { exit found ? 0 : 1 }' serial="$DEVICE_SERIAL"; then
      fail "Android device '$DEVICE_SERIAL' is not connected and ready according to adb devices."
    fi
    BUILD_ARGS+=("-p:AndroidDevice=$DEVICE_SERIAL")
  fi

  BUILD_ARGS+=("-t:Run")
fi

printf 'Running: dotnet build %s\n' "${BUILD_ARGS[*]}"
dotnet build "${BUILD_ARGS[@]}"
