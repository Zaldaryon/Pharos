#!/usr/bin/env bash
set -euo pipefail

# Configure Mesa software rasterization (llvmpipe)
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe
export MESA_LOADER_DRIVER_OVERRIDE=llvmpipe
export LIBGL_ALWAYS_INDIRECT=0
export MESA_GL_VERSION_OVERRIDE=${MESA_GL_VERSION_OVERRIDE:-4.5}
export MESA_GLSL_VERSION_OVERRIDE=${MESA_GLSL_VERSION_OVERRIDE:-450}

# Configure OpenAL null audio backend
export ALSOFT_DRIVERS=null

echo "==> Configuring Mesa software rendering (llvmpipe)..."
echo "    LIBGL_ALWAYS_SOFTWARE=$LIBGL_ALWAYS_SOFTWARE"
echo "    GALLIUM_DRIVER=$GALLIUM_DRIVER"
echo "    MESA_GL_VERSION_OVERRIDE=$MESA_GL_VERSION_OVERRIDE"
echo "    MESA_GLSL_VERSION_OVERRIDE=$MESA_GLSL_VERSION_OVERRIDE"

# Anchor directory to repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Determine virtual display execution
if command -v xvfb-run >/dev/null 2>&1; then
    echo "==> Executing test suite inside xvfb-run (1280x720x24)..."
    xvfb-run -a -s "-screen 0 1280x720x24" dotnet test "$REPO_DIR/Pharos.sln" "$@"
else
    if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
        echo "ERROR: Neither DISPLAY nor xvfb-run is available. Please install xvfb (e.g. apt-get install -y xvfb) or set DISPLAY." >&2
        exit 1
    fi
    echo "==> Executing test suite on DISPLAY=${DISPLAY:-wayland}..."
    dotnet test "$REPO_DIR/Pharos.sln" "$@"
fi
