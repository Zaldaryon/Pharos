#!/usr/bin/env bash
# Fails when the pharos tool package carries any file of the Vintage Story install: the tool runs
# against the user's own install and must never redistribute the game.
#
# Usage: scripts/check-tool-package.sh <Zaldaryon.Pharos.Cli.nupkg> <Vintage Story install>
set -euo pipefail

package="$1"
game="$2"

# The tool's own NuGet dependencies may share a name with the game's copy.
allowed='^(Newtonsoft\.Json\.dll)$'

game_files=$(cd "$game" && { ls -1; [ -d Lib ] && ls -1 Lib; } | sort -u)
package_files=$(unzip -Z1 "$package" | xargs -n1 basename | sort -u)

offending=$(comm -12 <(echo "$game_files") <(echo "$package_files") | grep -Ev "$allowed" || true)
if [ -n "$offending" ]; then
  echo "::error::$(basename "$package") carries files of the Vintage Story install:"
  echo "$offending"
  exit 1
fi

echo "$(basename "$package") carries no file of the Vintage Story install."
