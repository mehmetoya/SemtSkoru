#!/bin/bash
# Manual run of the local İBB ingestion runner.
#
#   run-now.sh                 kick the LaunchAgent now - exactly a scheduled run (--due), in the
#                              background, output to the log
#   run-now.sh all             run every job now, in this terminal (ignores --due)
#   run-now.sh parking traffic run just these jobs now, in this terminal
#   run-now.sh --list          show each job's last successful sync and whether it is due
#
# Terminal runs are also appended to the log file.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "$SCRIPT_DIR/common.sh"

if [[ $# -eq 0 ]]; then
  is_loaded || die "the agent is not installed; run $SCRIPT_DIR/install.sh first"
  if is_running; then
    echo "A run is already in progress. Follow it with: tail -f \"$LOG_FILE\""
    exit 0
  fi
  launchctl kickstart "$SERVICE"
  echo "Started. Follow it with: tail -f \"$LOG_FILE\""
  exit 0
fi

[[ -x "$WRAPPER" ]] || die "the runner is not installed; run $SCRIPT_DIR/install.sh first"
mkdir -p "$LOG_DIR"
set +e
"$WRAPPER" "$@" 2>&1 | tee -a "$LOG_FILE"
status="${PIPESTATUS[0]}"
set -e
exit "$status"
