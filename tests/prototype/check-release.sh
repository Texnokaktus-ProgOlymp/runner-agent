#!/bin/sh
# Exercise release ordering against a disposable local Git remote and fake registry.
set -eu
repo=$(pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT HUP INT TERM
mkdir "$scratch/bin"
cat > "$scratch/bin/docker" <<'MOCK'
#!/bin/sh
printf '%s\n' "$*" >> "$DOCKER_CALLS"
case "$*" in push*) [ "${FAIL_PUSH:-0}" = 0 ] ;; esac
MOCK
chmod +x "$scratch/bin/docker"
export PATH="$scratch/bin:$PATH" DOCKER_CALLS="$scratch/docker-calls"
git init -q --bare "$scratch/remote.git"
git init -q "$scratch/work"
cd "$scratch/work"
git config user.name 'release test'
git config user.email 'release@example.invalid'
git remote add origin "$scratch/remote.git"
GIT_AUTHOR_DATE=2020-01-01T22:00:00Z GIT_COMMITTER_DATE=2020-01-01T22:00:00Z git commit -q --allow-empty -m fixture
export GITHUB_SHA=$(git rev-parse HEAD) GITHUB_RUN_NUMBER=17
version=$(sh "$repo/scripts/release-version.sh")
test "$version" = 2020.1.2.17
export RELEASE_VERSION=$version
sh "$repo/scripts/publish-images.sh"
test "$(git ls-remote origin "refs/tags/v$version" | cut -f1)" = "$GITHUB_SHA"
test "$(grep -c '^push ' "$DOCKER_CALLS")" = 2
sh "$repo/scripts/publish-images.sh"
test "$(grep -c '^push ' "$DOCKER_CALLS")" = 4
# A conflict must fail before any registry mutation.
git commit -q --allow-empty -m other
export GITHUB_SHA=$(git rev-parse HEAD)
if sh "$repo/scripts/publish-images.sh"; then exit 1; fi
test "$(grep -c '^push ' "$DOCKER_CALLS")" = 4
# A failed image push must leave no release tag.
export RELEASE_VERSION=2020.1.2.18 FAIL_PUSH=1
if sh "$repo/scripts/publish-images.sh"; then exit 1; fi
test -z "$(git ls-remote origin refs/tags/v2020.1.2.18)"
export RELEASE_VERSION=2020.1.2.19 FAIL_PUSH=0
cat > "$scratch/remote.git/hooks/pre-receive" <<'HOOK'
#!/bin/sh
exit 1
HOOK
chmod +x "$scratch/remote.git/hooks/pre-receive"
before=$(grep -c '^push ' "$DOCKER_CALLS")
if sh "$repo/scripts/publish-images.sh"; then exit 1; fi
test "$(grep -c '^push ' "$DOCKER_CALLS")" = "$((before + 2))"
test -z "$(git ls-remote origin refs/tags/v2020.1.2.19)"
! grep -Eq '^(rmi|image rm|manifest rm) ' "$DOCKER_CALLS"
echo 'Release checks passed: Moscow date, retry, conflict, failed image/tag push.'
