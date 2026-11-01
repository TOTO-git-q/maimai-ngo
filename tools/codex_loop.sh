#!/usr/bin/env bash
# Re-launch the same Codex task until its worklog ends with DONE or BLOCKED (max N runs, max T seconds in total).
# usage: run_until_done.sh <task.md> <worklog.md> <log> <workdir> [max_runs] [max_seconds]
TASK="$1"; WL="$2"; LOG="$3"; WD="$4"; MAX="${5:-12}"; TMAX="${6:-7200}"
START=$(date +%s)
for i in $(seq 1 "$MAX"); do
  echo "=== run $i $(date '+%T')" | tee -a "$LOG.loop"
  bash "$HOME/.claude/skills/call-gpt/scripts/codex_exec.sh" -t "$TASK" -m gpt-6.1-terra -e xhigh -s workspace-write -C "$WD" -l "$LOG" > /dev/null 2>&1
  last=$(grep -v '^[[:space:]]*$' "$WD/$WL" | tail -1)
  echo "    worklog last line: ${last:0:200}" | tee -a "$LOG.loop"
  case "$last" in DONE*|BLOCKED*) echo "=== finished: ${last:0:40}" | tee -a "$LOG.loop"; exit 0 ;; esac
  [ $(( $(date +%s) - START )) -ge "$TMAX" ] && { echo "=== time budget reached" | tee -a "$LOG.loop"; exit 3; }
done
echo "=== run budget reached" | tee -a "$LOG.loop"; exit 4
