#!/usr/bin/env bash
# Runs one test executable from the CI package (see .github/workflows/ci.yml) and makes its result diagnosable:
#   - writes the results as JUnit XML to test-results/<project>.xml (CI uploads that folder);
#   - writes a short summary to the job summary;
#   - FAILS when the executable ran no tests or skipped all of them, which a test runner reports as success (for example
#     when a level was deleted, a filter matched nothing, or the model or the image were not provided).
#
# Usage: scripts/ci-run-tests.sh <TestProject>   (run from the folder where the package was unpacked)
set -uo pipefail

project="${1:?Usage: ci-run-tests.sh <TestProject>, for example Tinker.UnitTests}"
executable="tests/${project}/bin/Release/net10.0/${project}"
results="test-results/${project}.xml"

mkdir -p test-results
"$executable" -result-junit "$results"
status=$?

# The first <testsuites> element carries the totals.
attribute() { grep -o -m1 "<testsuites [^>]*" "$results" 2>/dev/null | grep -o "$1=\"[0-9]*\"" | head -1 | grep -o '[0-9]*'; }
total="$(attribute tests)"
failures="$(attribute failures)"
errors="$(attribute errors)"
skipped="$(grep -o '<skipped' "$results" 2>/dev/null | wc -l | tr -d ' ')"
total="${total:-0}"
failures="${failures:-0}"
errors="${errors:-0}"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### ${project}"
    echo
    echo "| Tests | Failures | Errors |"
    echo "| ---: | ---: | ---: |"
    echo "| ${total} | ${failures} | ${errors} |"
    echo
    echo "Skipped: ${skipped}"
  } >> "$GITHUB_STEP_SUMMARY"
fi

if [ "$total" -eq 0 ]; then
  echo "::error title=${project}::No tests were run. A test level that runs nothing must not pass: check that the test project still has tests and that the results file ${results} was written."
  exit 1
fi

# A level whose tests were all skipped (for example the model files or the image were not provided) proved nothing.
if [ "$skipped" -ge "$total" ]; then
  echo "::error title=${project}::All ${total} tests were skipped. A skipped level proves nothing: check that what the level needs (the model files, the image) was provided."
  exit 1
fi

exit "$status"
