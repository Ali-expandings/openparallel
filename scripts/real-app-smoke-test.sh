#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP=""
INSTANCES=2
KEEP_RUNNING=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --app)
      APP="${2:-}"
      shift 2
      ;;
    --instances)
      INSTANCES="${2:-2}"
      shift 2
      ;;
    --keep-running)
      KEEP_RUNNING=1
      shift
      ;;
    *)
      printf 'Unknown option: %s\n' "$1" >&2
      exit 64
      ;;
  esac
done

[[ -d "$APP" && "${APP##*.}" == "app" ]] || {
  printf 'usage: %s --app /path/App.app [--instances 2] [--keep-running]\n' "$0" >&2
  exit 64
}
[[ "$INSTANCES" =~ ^[1-9][0-9]*$ ]] || {
  printf 'instances must be a positive integer\n' >&2
  exit 64
}

BIN="$ROOT/.build/debug/OpenParallel"
[[ -x "$BIN" ]] || swift build --package-path "$ROOT" >/dev/null

PLIST="$APP/Contents/Info.plist"
EXECUTABLE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$PLIST")"
TARGET_EXE="$APP/Contents/MacOS/$EXECUTABLE_NAME"
APP_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDisplayName' "$PLIST" 2>/dev/null || /usr/libexec/PlistBuddy -c 'Print :CFBundleName' "$PLIST")"
BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PLIST" 2>/dev/null || true)"
VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$PLIST" 2>/dev/null || true)"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
SAFE_APP="$(printf '%s' "$APP_NAME" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9' '-' | sed 's/^-//;s/-$//')"
LAB="$HOME/Library/Application Support/OpenParallel/TestLab/smoke/$RUN_ID-$SAFE_APP"
OUT="$LAB/shortcuts"
RESULT="$LAB/result.json"
mkdir -p "$OUT"

INSPECT="$($BIN inspect --src "$APP" --json)"
SUPPORTS="$(/usr/bin/jq -r '.supportsProfileIsolation' <<< "$INSPECT")"
FAMILY="$(/usr/bin/jq -r '.family' <<< "$INSPECT")"
[[ "$SUPPORTS" == "true" ]] || {
  /usr/bin/jq -n \
    --arg app "$APP" --arg family "$FAMILY" --arg reason "No profile-isolation recipe" \
    '{passed:false, app:$app, family:$family, reason:$reason}' > "$RESULT"
  cat "$RESULT"
  exit 2
}

SIGNATURE_VERIFICATION="strict"
if ! /usr/bin/codesign --verify --deep --strict "$APP" 2>/dev/null; then
  /usr/bin/codesign --verify --deep "$APP"
  SIGNATURE_VERIFICATION="basic"
fi

declare -a WRAPPERS=()
declare -a PROFILE_ROOTS=()
declare -a INSTANCE_EXES=()
declare -a PIDS=()
LAUNCH_MODE="linked-signed-source"

cleanup_test_processes() {
  [[ "$KEEP_RUNNING" == "1" ]] && return

  declare -a cleanup_pids=("${PIDS[@]}")
  local profile_root wrapper candidate_pid command_line extended_command
  for profile_root in "${PROFILE_ROOTS[@]}"; do
    while read -r candidate_pid command_line; do
      [[ "$command_line" == "$TARGET_EXE"* ]] || continue
      extended_command="$(/bin/ps eww -p "$candidate_pid" -o command= 2>/dev/null || true)"
      if [[ "$command_line" == *"$profile_root"* || "$extended_command" == *"$profile_root"* ]]; then
        cleanup_pids+=("$candidate_pid")
      fi
    done < <(/bin/ps -axo pid=,command=)
  done
  for wrapper in "${WRAPPERS[@]}"; do
    while read -r candidate_pid; do
      [[ -n "$candidate_pid" ]] && cleanup_pids+=("$candidate_pid")
    done < <(/usr/bin/pgrep -f "$wrapper/Contents/MacOS/openparallel-launch" 2>/dev/null || true)
  done
  local instance_exe
  for instance_exe in "${INSTANCE_EXES[@]}"; do
    while read -r candidate_pid command_line; do
      [[ "$command_line" == "$instance_exe"* ]] && cleanup_pids+=("$candidate_pid")
    done < <(/bin/ps -axo pid=,command=)
  done
  if [[ ${#cleanup_pids[@]} -gt 0 ]]; then
    /bin/kill "${cleanup_pids[@]}" 2>/dev/null || true
  fi
}
trap cleanup_test_processes EXIT

find_profile_pid() {
  local profile_root="$1"
  local instance_exe="$2"
  local candidate_pid command_line extended_command

  # A native identity copy runs its own executable inside the generated bundle,
  # so the executable path alone already identifies the instance.
  if [[ "$LAUNCH_MODE" == "native-identity-copy" ]]; then
    while read -r candidate_pid command_line; do
      if [[ "$command_line" == "$instance_exe"* ]]; then
        printf '%s\n' "$candidate_pid"
        return 0
      fi
    done < <(/bin/ps -axo pid=,command=)
    return 1
  fi

  while read -r candidate_pid command_line; do
    [[ "$command_line" == "$TARGET_EXE"* ]] || continue
    if [[ "$command_line" == *"$profile_root"* ]]; then
      printf '%s\n' "$candidate_pid"
      return 0
    fi
    extended_command="$(/bin/ps eww -p "$candidate_pid" -o command= 2>/dev/null || true)"
    if [[ "$extended_command" == *"$profile_root"* ]]; then
      printf '%s\n' "$candidate_pid"
      return 0
    fi
  done < <(/bin/ps -axo pid=,command=)
  return 1
}

for index in $(seq 1 "$INSTANCES"); do
  NAME="OP Smoke $APP_NAME $RUN_ID $index"
  BUILD_OUTPUT="$($BIN build --src "$APP" --name "$NAME" --out "$OUT")"
  WRAPPER="$(printf '%s\n' "$BUILD_OUTPUT" | sed -n 's/^wrapper=//p')"
  SLUG="$(/usr/libexec/PlistBuddy -c 'Print :OpenParallelInstanceSlug' "$WRAPPER/Contents/Info.plist")"
  PROFILE_ROOT="$HOME/Library/Application Support/OpenParallel/Instances/$SLUG"
  LAUNCH_MODE="$(/usr/libexec/PlistBuddy -c 'Print :OpenParallelLaunchMode' "$WRAPPER/Contents/Info.plist" 2>/dev/null || printf 'linked-signed-source')"
  INSTANCE_EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$WRAPPER/Contents/Info.plist")"
  WRAPPERS+=("$WRAPPER")
  PROFILE_ROOTS+=("$PROFILE_ROOT")
  INSTANCE_EXES+=("$WRAPPER/Contents/MacOS/$INSTANCE_EXE_NAME")
  /usr/bin/open -n "$WRAPPER"
done

for index in "${!PROFILE_ROOTS[@]}"; do
  profile_root="${PROFILE_ROOTS[$index]}"
  instance_exe="${INSTANCE_EXES[$index]}"
  PID=""
  for _ in $(seq 1 100); do
    PID="$(find_profile_pid "$profile_root" "$instance_exe" || true)"
    [[ -n "$PID" ]] && break
    /bin/sleep 0.25
  done
  [[ -n "$PID" ]] || {
    printf 'No isolated process found for %s\n' "$profile_root" >&2
    exit 1
  }
  PIDS+=("$PID")
done

UNIQUE_PIDS="$(printf '%s\n' "${PIDS[@]}" | sort -u | wc -l | tr -d ' ')"
[[ "$UNIQUE_PIDS" == "$INSTANCES" ]]
for profile_root in "${PROFILE_ROOTS[@]}"; do
  [[ -d "$profile_root" ]]
done

PIDS_JSON="$(printf '%s\n' "${PIDS[@]}" | /usr/bin/jq -Rsc 'split("\n") | map(select(length > 0) | tonumber)')"
ROOTS_JSON="$(printf '%s\n' "${PROFILE_ROOTS[@]}" | /usr/bin/jq -Rsc 'split("\n") | map(select(length > 0))')"
/usr/bin/jq -n \
  --arg testedAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg app "$APP" \
  --arg appName "$APP_NAME" \
  --arg bundleIdentifier "$BUNDLE_ID" \
  --arg version "$VERSION" \
  --arg family "$FAMILY" \
  --arg signatureVerification "$SIGNATURE_VERIFICATION" \
  --arg launchMode "$LAUNCH_MODE" \
  --argjson instances "$INSTANCES" \
  --argjson pids "$PIDS_JSON" \
  --argjson profileRoots "$ROOTS_JSON" \
  '{schemaVersion:1, passed:true, approval:"process-and-profile", testedAt:$testedAt, app:$app, appName:$appName, bundleIdentifier:$bundleIdentifier, version:$version, family:$family, launchMode:$launchMode, instances:$instances, pids:$pids, profileRoots:$profileRoots, sourceSignatureValid:true, signatureVerification:$signatureVerification}' > "$RESULT"

cat "$RESULT"
printf 'result=%s\n' "$RESULT"
