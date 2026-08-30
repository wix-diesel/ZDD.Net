#!/usr/bin/env bash
# Installs the .NET SDK needed to build and test this repository.
#
# NOTE: dotnet-install.sh (builds.dotnet.microsoft.com) is blocked by the egress
# policy of the Claude Code remote environment, so we install from the Ubuntu
# archive instead, which carries dotnet-sdk-10.0. That SDK builds both target
# frameworks of this repository (netstandard2.0 and net10.0).
set -euo pipefail

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -q '^10\.'; then
    exit 0
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo "setup-dev-env: apt-get not found; install the .NET 10 SDK manually." >&2
    exit 0
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq || true
apt-get install -y -qq dotnet-sdk-10.0

dotnet --list-sdks
