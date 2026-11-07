#!/bin/sh
set -eu
# Commit date, not runner date: rerunning a workflow keeps its version.
revision=${GITHUB_SHA:?GITHUB_SHA is required}
number=${GITHUB_RUN_NUMBER:?GITHUB_RUN_NUMBER is required}
case "$number" in ''|*[!0-9]*) exit 1 ;; esac
timestamp=$(git show -s --format=%ct "$revision")
date=$(TZ=Europe/Moscow date -d "@$timestamp" +%Y.%-m.%-d)
printf '%s.%s\n' "$date" "$number"
