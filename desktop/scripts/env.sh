# Shared environment for building and running the Windows version on macOS.
# Source it: . windows/scripts/env.sh
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# The Sikarugir test bottle (made with Sikarugir Creator, engine WS12WineCX24.0.7_5).
export OP_BOTTLE="${OP_BOTTLE:-$HOME/Applications/Sikarugir/OpenParallel-Test.app}"
export WINEPREFIX="$OP_BOTTLE/Contents/SharedSupport/prefix"
export DYLD_FALLBACK_LIBRARY_PATH="$OP_BOTTLE/Contents/Frameworks:$OP_BOTTLE/Contents/SharedSupport/wine/lib:/usr/lib"
export WINEDEBUG="${WINEDEBUG:--all}"
OP_WINE="$OP_BOTTLE/Contents/SharedSupport/wine/bin/wine"
export OP_WINE
