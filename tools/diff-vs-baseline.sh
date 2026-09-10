#!/usr/bin/env bash
# Diff a snapshot against the pre-1.0 baseline to see exactly what changed.
#   usage: tools/diff-vs-baseline.sh 1.0 [ClassName]
set -euo pipefail
W="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LABEL="${1:?usage: diff-vs-baseline.sh <label> [ClassName]}"
NEW="$W/snapshots/$LABEL/src"
OLD="$W/baseline/src"
[ -d "$NEW" ] || { echo "no snapshot '$LABEL' -- run tools/snapshot.sh $LABEL"; exit 1; }

if [ $# -ge 2 ]; then
  diff -u "$OLD/$2.cs" "$NEW/$2.cs" || true
  exit 0
fi

echo "=== REMOVED types (patches targeting these will hard-fail) ==="
comm -23 <(cd "$OLD" && find . -name '*.cs' | sort) <(cd "$NEW" && find . -name '*.cs' | sort)
echo
echo "=== NEW types ==="
comm -13 <(cd "$OLD" && find . -name '*.cs' | sort) <(cd "$NEW" && find . -name '*.cs' | sort)
echo
echo "=== CHANGED types ==="
comm -12 <(cd "$OLD" && find . -name '*.cs' | sort) <(cd "$NEW" && find . -name '*.cs' | sort) \
  | while read -r f; do cmp -s "$OLD/$f" "$NEW/$f" || echo "${f#./}"; done
