#!/bin/sh
set -eu
if ! /bin/sh /usr/local/lib/runner-agent/environment.sh; then
    echo 'RunnerAgent sandbox initialization failed.' >&2
    exit 1
fi
exec dotnet /app/Texnokaktus.ProgOlymp.RunnerAgent.dll "$@"
