#!/usr/bin/env sh
# Starts a Vintage Story dedicated server configured for Pharos tests.
#
#   VS_INSTALL              folder holding VintagestoryServer.dll (required)
#   VS_DATA                 data folder: config, saves, logs (default: ./pharos-server-data)
#   VS_PORT                 TCP and UDP port (default: 42420)
#   VS_VERIFY_PLAYER_AUTH   true to have the auth server validate players (default: false)
#   VS_WORLD_TYPE           superflat, standard, ... (default: superflat)
#   VS_PLAYSTYLE            creativebuilding, surviveandbuild, ... (default: creativebuilding)
#
# The server prints "Dedicated Server now running" once it accepts players.
set -eu

: "${VS_INSTALL:?Set VS_INSTALL to the folder that holds VintagestoryServer.dll}"
VS_DATA="${VS_DATA:-./pharos-server-data}"
VS_PORT="${VS_PORT:-42420}"
VS_VERIFY_PLAYER_AUTH="${VS_VERIFY_PLAYER_AUTH:-false}"
VS_WORLD_TYPE="${VS_WORLD_TYPE:-superflat}"
VS_PLAYSTYLE="${VS_PLAYSTYLE:-creativebuilding}"

mkdir -p "$VS_DATA"

# WhitelistMode 1 is Off: a dedicated server otherwise admits only whitelisted players.
exec dotnet "$VS_INSTALL/VintagestoryServer.dll" \
    --dataPath "$VS_DATA" \
    --port "$VS_PORT" \
    --withconfig "{ VerifyPlayerAuth: $VS_VERIFY_PLAYER_AUTH, WhitelistMode: 1, ServerName: 'Pharos test server', AllowPvP: false, WorldConfig: { WorldName: 'Pharos', PlayStyle: '$VS_PLAYSTYLE', WorldType: '$VS_WORLD_TYPE', AllowCreativeMode: true } }"
