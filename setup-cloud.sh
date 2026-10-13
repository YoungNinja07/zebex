#!/usr/bin/env bash
set -euo pipefail
SDK_ROOT=/workspace/.zebex-tools/dotnet
mkdir -p /workspace/.zebex-tools
if [ ! -x "$SDK_ROOT/dotnet" ] || [ "$("$SDK_ROOT/dotnet" --version)" != '8.0.425' ]; then
  SDK_ARCHIVE=$(mktemp /tmp/zebex-sdk.XXXXXX.tar.gz)
  trap 'rm -f "$SDK_ARCHIVE"' EXIT
  curl --fail --show-error --location https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.425/dotnet-sdk-8.0.425-linux-x64.tar.gz --output "$SDK_ARCHIVE"
  printf '%s  %s\n' '934b8060a7190e5909ad1fd0785db542f487b3bbf6cdd14826b02095fdd0d0394298b1634085eff302928fccc33f7c1a7253e9b87df555fc36fce819bcd2e798' "$SDK_ARCHIVE" | sha512sum --check
  mkdir -p "$SDK_ROOT"
  tar -xzf "$SDK_ARCHIVE" -C "$SDK_ROOT"
fi
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.zebex-tools/dotnet-home
export NUGET_PACKAGES=/workspace/.zebex-tools/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
cd /workspace/zebex
dotnet run --project tests/Zebex.Checks -c Release
dotnet run --project tests/Transfer.Checks -c Release
dotnet run --project tests/Ekp.Checks -c Release
dotnet run --project tests/Protocol.Checks -c Release
dotnet publish src/Zebex.SuzBridge -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o artifacts/windows-x64/suz-bridge
dotnet publish src/Zebex.Simulator -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/windows-x64
cp artifacts/windows-x64/suz-bridge/ZebexSuzBridge.exe artifacts/windows-x64/suz-bridge/zac_dataReader.exe
cp artifacts/windows-x64/suz-bridge/ZebexSuzBridge.exe artifacts/windows-x64/suz-bridge/zac_dlookup.exe
dotnet run --project tests/Protocol.Checks -c Release -- --manifest artifacts/windows-x64/suz-bridge
cp docs/START-Windows.md artifacts/windows-x64/INSTRUKCJA.md
cp README.md artifacts/windows-x64/README.md
cp examples/produkty.csv artifacts/windows-x64/produkty.csv
