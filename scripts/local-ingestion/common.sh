# Shared settings for install.sh / uninstall.sh / run-now.sh. Sourced, not executed.
# shellcheck shell=bash

LABEL="com.semtskoru.ingest"
KEYCHAIN_SERVICE="semtskoru-supabase"

# Outside ~/Documents on purpose: macOS privacy controls (TCC) block launchd jobs from reading
# ~/Documents, ~/Desktop and ~/Downloads, so the runner must not live in the repo checkout.
APP_SUPPORT_DIR="$HOME/Library/Application Support/SemtSkoru"
INSTALL_DIR="$APP_SUPPORT_DIR/ingest"
APP_DIR="$INSTALL_DIR/app"
WRAPPER="$INSTALL_DIR/run-ingest.sh"

LOG_DIR="$HOME/Library/Logs/SemtSkoru"
LOG_FILE="$LOG_DIR/ingest.log"

PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOMAIN="gui/$(id -u)"
SERVICE="$DOMAIN/$LABEL"

die() {
  echo "error: $*" >&2
  exit 1
}

is_loaded() {
  launchctl print "$SERVICE" >/dev/null 2>&1
}

is_running() {
  launchctl print "$SERVICE" 2>/dev/null | grep -q "state = running"
}
