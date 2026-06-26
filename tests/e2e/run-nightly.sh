#!/usr/bin/env bash
# Nightly E2E mega suite — pull repo + run all specs + mail report
# Wywoływany przez cron na R620: 03:00 codziennie
# Logi i raport idą do ~/sportrental-e2e-runs/
set -e

REPO_DIR="$HOME/source/repos/SportRental"
LOG_DIR="$HOME/sportrental-e2e-runs"
TS=$(date +%Y%m%d-%H%M%S)
RUN_DIR="$LOG_DIR/run-$TS"
mkdir -p "$RUN_DIR"
LOG="$RUN_DIR/run.log"

echo "[$TS] Starting nightly E2E run" | tee -a "$LOG"

cd "$REPO_DIR"
git fetch --all --quiet 2>&1 | tee -a "$LOG"
git reset --hard origin/main 2>&1 | tee -a "$LOG"
COMMIT=$(git rev-parse --short HEAD)
echo "Commit: $COMMIT" | tee -a "$LOG"

cd tests/e2e
npm install --silent 2>&1 | tail -3 | tee -a "$LOG"

# Run all specs across 5 projects, 4 workers
START=$(date +%s)
npx playwright test --workers=4 --reporter=line > "$RUN_DIR/playwright.log" 2>&1
EXIT_CODE=$?
END=$(date +%s)
DURATION=$((END - START))

# Parse summary
SUMMARY=$(tail -10 "$RUN_DIR/playwright.log" | grep -E "passed|failed|skipped|did not run" | tail -1)
PASSED=$(echo "$SUMMARY" | grep -oE '[0-9]+ passed' | head -1 || echo "0 passed")
FAILED=$(echo "$SUMMARY" | grep -oE '[0-9]+ failed' | head -1 || echo "0 failed")

echo "[$TS] Done: exit=$EXIT_CODE duration=${DURATION}s $PASSED $FAILED" | tee -a "$LOG"

# Copy HTML report
if [ -d playwright-report ]; then
    cp -r playwright-report "$RUN_DIR/playwright-report"
fi

# Status file for monitoring (status.aidamian.uk style)
echo "{\"timestamp\":\"$TS\",\"commit\":\"$COMMIT\",\"exit_code\":$EXIT_CODE,\"duration_seconds\":$DURATION,\"summary\":\"$SUMMARY\"}" > "$LOG_DIR/latest-status.json"

# Email alert tylko na fail
if [ $EXIT_CODE -ne 0 ]; then
    MAIL_TO="hdtdtr@gmail.com"
    SUBJECT="[SportRental E2E] FAIL — $PASSED $FAILED at $TS (commit $COMMIT)"
    BODY="E2E suite zakończony niepowodzeniem.

Commit: $COMMIT
Timestamp: $TS
Duration: ${DURATION}s
Exit code: $EXIT_CODE

Summary: $SUMMARY

Logi: $RUN_DIR/playwright.log
Raport HTML: $RUN_DIR/playwright-report/index.html

Ostatnie 20 linii logu:
$(tail -20 "$RUN_DIR/playwright.log")
"
    if command -v mail >/dev/null 2>&1; then
        echo "$BODY" | mail -s "$SUBJECT" "$MAIL_TO" 2>&1 | tee -a "$LOG"
        echo "Mail sent to $MAIL_TO" | tee -a "$LOG"
    else
        echo "mail(1) niedostępne — alert nie wysłany. Body w logu." | tee -a "$LOG"
        echo "$BODY" >> "$LOG"
    fi
fi

# Pruning — zachowaj 30 ostatnich runów
ls -dt "$LOG_DIR"/run-* 2>/dev/null | tail -n +31 | xargs -r rm -rf

exit $EXIT_CODE
