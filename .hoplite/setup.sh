#!/usr/bin/env bash
# Installs the .NET 10 SDK in Linux sandboxes. Aisa only runs on Windows, but the headless libraries
# and their tests run here, and the WPF app compiles via EnableWindowsTargeting.
set -euo pipefail
DOTNET_DIR="${HOME}/.dotnet"
if [ ! -x "${DOTNET_DIR}/dotnet" ] || ! "${DOTNET_DIR}/dotnet" --list-sdks | grep -q '^10\.'; then
  mkdir -p "${DOTNET_DIR}"
  curl -sSL -o /tmp/dotnet-sdk.tgz https://aka.ms/dotnet/10.0/dotnet-sdk-linux-x64.tar.gz
  tar -xzf /tmp/dotnet-sdk.tgz -C "${DOTNET_DIR}"
  rm -f /tmp/dotnet-sdk.tgz
fi
"${DOTNET_DIR}/dotnet" --list-sdks
