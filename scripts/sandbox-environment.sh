#!/bin/sh
# Run inside the dedicated privileged agent container with a private cgroup namespace.
set -eu
grep -qx '0::/' /proc/self/cgroup
test -f /sys/fs/cgroup/cgroup.controllers
mkdir /sys/fs/cgroup/runner-agent-control
while read -r pid; do
    if [ "$pid" -gt 0 ]; then
        echo "$pid" > /sys/fs/cgroup/runner-agent-control/cgroup.procs
    fi
done < /sys/fs/cgroup/cgroup.procs
echo '+cpu +memory +pids' > /sys/fs/cgroup/cgroup.subtree_control
mkdir /sys/fs/cgroup/runner-agent-boxes
echo '+cpu +memory +pids' > /sys/fs/cgroup/runner-agent-boxes/cgroup.subtree_control
cat > /etc/isolate <<'CONFIG'
box_root = /var/lib/isolate
lock_root = /run/isolate/locks
cg_root = /sys/fs/cgroup/runner-agent-boxes
first_uid = 60000
first_gid = 60000
num_boxes = 1000
restricted_init = 1
CONFIG
isolate --check-config
mkdir -p /var/lib/runner-agent-dev
[ -e /var/lib/runner-agent-dev/null ] || mknod /var/lib/runner-agent-dev/null c 1 3
[ -e /var/lib/runner-agent-dev/zero ] || mknod /var/lib/runner-agent-dev/zero c 1 5
[ -e /var/lib/runner-agent-dev/random ] || mknod /var/lib/runner-agent-dev/random c 1 8
[ -e /var/lib/runner-agent-dev/urandom ] || mknod /var/lib/runner-agent-dev/urandom c 1 9
chmod 666 /var/lib/runner-agent-dev/*
