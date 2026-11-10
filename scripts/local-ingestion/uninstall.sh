#!/bin/bash
# Removes the local İBB ingestion LaunchAgent and the published runner.
# Keeps the Keychain item and the log file (printed below, with how to remove them too).
#
# Remember: with the agent gone NOTHING ingests İBB data any more (Render can't reach İBB) -
# air quality and parking go stale within days and /health/data starts answering 503.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

if is_loaded; then
  # bootout sends SIGTERM; the runner cancels the in-flight job cleanly.
  launchctl bootout "$SERVICE" 2>/dev/null || true
  echo "Unloaded $LABEL"
fi

rm -f "$PLIST"
rm -rf "$INSTALL_DIR"
rmdir "$APP_SUPPORT_DIR" 2>/dev/null || true

cat <<EOF
Removed the LaunchAgent ($PLIST) and the runner ($INSTALL_DIR).

Kept (remove by hand if you want):
  Log file      : rm "$LOG_FILE"
  Keychain item : security delete-generic-password -s $KEYCHAIN_SERVICE
EOF
