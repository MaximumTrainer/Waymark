#!/bin/sh
# Points this clone's git at the versioned hooks in .githooks/.
#
# core.hooksPath is per-clone and cannot be committed, so every clone runs this once. Versioning
# the hooks themselves means a change to them is reviewed like any other change, which copying
# scripts into .git/hooks would not give.
set -e

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT"

git config core.hooksPath .githooks

# Git ignores a hook that is not executable. On Windows the bit is carried in the index, so set it
# there too rather than only on disk.
for hook in .githooks/*; do
    case "$hook" in
        *.md) continue ;;
    esac
    chmod +x "$hook" 2>/dev/null || true
    git update-index --chmod=+x "$hook" 2>/dev/null || true
done

echo "Git hooks installed: core.hooksPath -> .githooks"
echo
echo "  pre-commit  secret scan, lint and build for the areas you touched"
echo "  commit-msg  Conventional Commits"
echo "  pre-push    backend and frontend test suites"
echo
echo "Bypass any of them once with SKIP_HOOKS=1, e.g.  SKIP_HOOKS=1 git commit ..."
