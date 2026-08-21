#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${NUGET_AUTH_TOKEN:-}" ]]; then
  echo "NUGET_AUTH_TOKEN is required to restore GitHub Packages." >&2
  exit 1
fi

source_name="github"
source_url="https://nuget.pkg.github.com/yaron-E92/index.json"

if dotnet nuget list source | grep -Fq "${source_name}"; then
  dotnet nuget update source "${source_name}" \
    --source "${source_url}" \
    --username "Yaron-E92" \
    --password "${NUGET_AUTH_TOKEN}" \
    --store-password-in-clear-text
else
  dotnet nuget add source "${source_url}" \
    --name "${source_name}" \
    --username "Yaron-E92" \
    --password "${NUGET_AUTH_TOKEN}" \
    --store-password-in-clear-text
fi
