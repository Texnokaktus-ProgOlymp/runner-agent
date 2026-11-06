#!/bin/sh
set -eu
. /usr/local/lib/runner-agent/environment.sh
exec dotnet /app/Texnokaktus.ProgOlymp.RunnerAgent.dll "$@"
