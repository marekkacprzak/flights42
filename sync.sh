#!/usr/bin/env bash
set -euo pipefail

# Usage: ./sync-upstream-new-branches.sh [--push] [--dry-run]
PUSH=false
DRY_RUN=false

for arg in "$@"; do
  case "$arg" in
    --push) PUSH=true ;;
    --dry-run) DRY_RUN=true ;;
    -h|--help)
      echo "Usage: $0 [--push] [--dry-run]"
      exit 0
      ;;
    *) ;;
  esac
done

echo "Fetching upstream..."
git fetch upstream --prune

branches=$(git for-each-ref --format='%(refname:short)' refs/remotes/upstream | sed 's|^upstream/||' | grep -v '^HEAD$')

if [ -z "$branches" ]; then
  echo "No upstream branches found."
  exit 0
fi

for b in $branches; do
  if git show-ref --verify --quiet "refs/heads/$b"; then
    echo "Skipping existing local branch: $b"
    continue
  fi

  echo "Creating local branch '$b' from upstream/$b"
  if [ "$DRY_RUN" = true ]; then
    echo "[DRY RUN] git checkout -b \"$b\" \"upstream/$b\""
  else
    git checkout -b "$b" "upstream/$b"
  fi

  if [ "$PUSH" = true ]; then
    echo "Pushing $b to origin"
    if [ "$DRY_RUN" = true ]; then
      echo "[DRY RUN] git push origin \"$b\""
    else
      git push origin "$b"
    fi
  fi
done

echo "Done."
