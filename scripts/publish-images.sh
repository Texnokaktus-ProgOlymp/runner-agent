#!/bin/sh
set -eu
version=${RELEASE_VERSION:?RELEASE_VERSION is required}
revision=${GITHUB_SHA:?GITHUB_SHA is required}
tag="v$version"
base=ghcr.io/texnokaktus-progolymp/runner-agent-base
cpp=ghcr.io/texnokaktus-progolymp/runner-agent-cpp-23
# Check conflicts before touching the registry. Never force a Git tag.
existing=$(git ls-remote --tags origin "refs/tags/$tag")
if [ -n "$existing" ]; then
    git fetch --no-tags origin "refs/tags/$tag"
    [ "$(git rev-parse 'FETCH_HEAD^{commit}')" = "$revision" ] || {
        echo 'Release tag already points to another commit.' >&2
        exit 1
    }
fi
docker tag runner-agent-base:local "$base:$version"
docker tag runner-agent-cpp-23:local "$cpp:$version"
docker push "$base:$version"
docker push "$cpp:$version"
if [ -z "$existing" ]; then
    git tag "$tag" "$revision"
    git push origin "refs/tags/$tag"
fi
