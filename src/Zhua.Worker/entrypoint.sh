#!/bin/sh
# Headed Chromium needs an X display, but a container/headless server (NAS/EC2) has none. We start Xvfb
# (an in-memory X server) DIRECTLY here rather than via xvfb-run, which hangs in non-TTY containers.
set -eu

display=:99
xvfb_log=/tmp/xvfb.log

# A stop/start of the SAME container keeps /tmp, and Xvfb refuses to start over a stale display-99
# lock/socket left by the previous run ("Server is already active for display 99") — that outage killed
# every scheduled crawl from 2026-07-14 (docs/internals/worker-xvfb-restart-incident.md). Clean first.
rm -f /tmp/.X99-lock /tmp/.X11-unix/X99

# Log to a file instead of /dev/null: quiet in the happy path (xkbcomp keymap warnings don't drown the
# Worker's [scheduled] logs), but recoverable below when Xvfb fails to come up.
Xvfb "$display" -screen 0 1920x1080x24 -nolisten tcp >"$xvfb_log" 2>&1 &
xvfb_pid=$!

# Wait until the display answers; bail out early if the Xvfb process itself died.
i=0
while [ "$i" -lt 30 ]; do
  if xdpyinfo -display "$display" >/dev/null 2>&1; then
    export DISPLAY="$display"
    # Hand off to the self-contained Worker. "$@" carries any CLI args (crawl / match / recon); none = scheduled.
    exec /app/Zhua.Worker "$@"
  fi

  if ! kill -0 "$xvfb_pid" 2>/dev/null; then
    break
  fi

  i=$((i + 1))
  sleep 0.2
done

# No display -> FAIL the container (never start the Worker: it would sit "healthy" while every headed
# Chromium launch dies with "Missing X server"). Exiting lets the restart policy retry a clean start.
echo "Xvfb failed to become ready on $display" >&2
cat "$xvfb_log" >&2
exit 1
