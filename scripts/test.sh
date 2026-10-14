#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TESTING_FRAMEWORK="/Library/Developer/CommandLineTools/Library/Developer/Frameworks"

cd "$ROOT"

if [[ -d "$TESTING_FRAMEWORK/Testing.framework" ]]; then
  DYLD_FRAMEWORK_PATH="$TESTING_FRAMEWORK" \
    swift test \
      -Xswiftc -F -Xswiftc "$TESTING_FRAMEWORK" \
      -Xlinker "-F$TESTING_FRAMEWORK"
else
  swift test
fi
