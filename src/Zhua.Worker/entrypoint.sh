#!/bin/sh
# Headed Chromium needs an X display, but a container/headless server (NAS/EC2) has none. We start Xvfb
# (an in-memory X server) DIRECTLY here rather than via xvfb-run, which hangs in non-TTY containers.
set -e

# Silence Xvfb's own stderr (harmless xkbcomp "keysym" keymap warnings on every browser launch) so it
# doesn't drown the Worker's [scheduled] logs. Our app's stdout/stderr is untouched.
Xvfb :99 -screen 0 1920x1080x24 -nolisten tcp >/dev/null 2>&1 &
export DISPLAY=:99

# Wait until the display answers (xdpyinfo if present; otherwise fall back to a short fixed wait).
i=0
while [ "$i" -lt 30 ]; do
  if xdpyinfo -display :99 >/dev/null 2>&1; then break; fi
  i=$((i + 1))
  sleep 0.2
done

# Hand off to the self-contained Worker. "$@" carries any CLI args (crawl / match / recon); none = scheduled.
exec /app/Zhua.Worker "$@"
