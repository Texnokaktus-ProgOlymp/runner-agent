#!/bin/sh
# Smoke the real Linux process. No MinIO, isolate, credentials or privileged mode needed.
set -eu
ulimit -c 0
dll=$(realpath "${1:?Usage: smoke.sh /path/to/RunnerAgent.dll}")
scratch=$(mktemp -d)
child=
cleanup() {
    if [ -n "$child" ] && kill -0 "$child" 2>/dev/null; then
        kill -KILL "$child"
        wait "$child" 2>/dev/null || true
    fi
    rm -rf "$scratch"
}
trap cleanup EXIT HUP INT TERM
mkdir "$scratch/workspace"
printf 'preserve\n' > "$scratch/workspace/unrelated.txt"
expect_start_failure() {
    set +e
    "$@" > "$scratch/failure.log" 2>&1
    code=$?
    set -e
    if [ "$code" -eq 0 ]; then
        cat "$scratch/failure.log"
        echo 'Expected startup failure, got exit 0' >&2
        exit 1
    fi
    grep -Eq 'SERVICE_NAME|Runner:WorkspaceRoot' "$scratch/failure.log"
}
expect_start_failure env -u SERVICE_NAME dotnet "$dll"
expect_start_failure env SERVICE_NAME=' ' dotnet "$dll"
expect_start_failure env SERVICE_NAME=runner-agent-cpp-23 Runner__WorkspaceRoot=relative dotnet "$dll"
expect_start_failure env SERVICE_NAME=runner-agent-cpp-23 Runner__WorkspaceRoot="$scratch/missing" dotnet "$dll"
env SERVICE_NAME=runner-agent-cpp-23 Runner__WorkspaceRoot="$scratch/workspace" \
    ASPNETCORE_URLS=http://127.0.0.1:0 dotnet "$dll" > "$scratch/host.log" 2>&1 &
child=$!
attempt=0
until grep -q 'Application started' "$scratch/host.log"; do
    if ! kill -0 "$child" 2>/dev/null || [ "$attempt" -ge 150 ]; then
        cat "$scratch/host.log"
        echo 'Host did not start' >&2
        exit 1
    fi
    sleep 0.1
    attempt=$((attempt + 1))
done
kill -TERM "$child"
attempt=0
while kill -0 "$child" 2>/dev/null; do
    if [ "$attempt" -ge 150 ]; then
        cat "$scratch/host.log"
        echo 'Host did not stop' >&2
        exit 1
    fi
    sleep 0.1
    attempt=$((attempt + 1))
done
wait "$child"
child=
grep -q '^preserve$' "$scratch/workspace/unrelated.txt"
[ "$(find "$scratch/workspace" -mindepth 1 | wc -l)" -eq 1 ]
[ ! -e "$scratch/missing" ]
echo 'Smoke passed: startup, invalid configuration, SIGTERM exit 0, existing files preserved.'
