#!/usr/bin/env bash
# create-issues.sh — bulk-create Waymark backlog issues using the GitHub CLI.
#
# Prerequisites:
#   gh auth login   (or set GH_TOKEN environment variable)
#
# Usage:
#   chmod +x create-issues.sh
#   ./create-issues.sh
#
# Each issue is created in the MaximumTrainer/Waymark repository.
# Re-running the script is safe: gh will create duplicates if the title already
# exists, so check the issue list before re-running.

set -euo pipefail

REPO="MaximumTrainer/Waymark"
ISSUES_DIR="$(dirname "$0")"

create_issue() {
  local file="$1"
  # Extract title from YAML front-matter (first "title:" line)
  local title
  title=$(grep -m1 '^title:' "$file" | sed 's/^title:[[:space:]]*"\(.*\)"/\1/')

  # Extract labels (comma-separated from the labels array)
  local labels
  labels=$(grep -m1 '^labels:' "$file" \
    | sed 's/^labels:[[:space:]]*\[//' \
    | sed 's/\]//' \
    | tr -d '"' \
    | tr -d ' ')

  # Body = everything after the closing --- front-matter delimiter
  local body
  body=$(awk '/^---$/{if(++c==2){found=1; next}} found{print}' "$file")

  echo "Creating: $title"
  gh issue create \
    --repo "$REPO" \
    --title "$title" \
    --label "$labels" \
    --body "$body"
}

for f in "$ISSUES_DIR"/*.md; do
  create_issue "$f"
done

echo ""
echo "Done. Visit https://github.com/$REPO/issues to review the created issues."
