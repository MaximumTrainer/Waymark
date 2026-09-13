<#
.SYNOPSIS
    Points this clone's git at the versioned hooks in .githooks/.

.DESCRIPTION
    core.hooksPath is per-clone and cannot be committed, so every clone runs this once.
    Windows equivalent of scripts/install-git-hooks.sh. The hooks themselves are POSIX sh and run
    under the sh that ships with Git for Windows.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

git config core.hooksPath .githooks

# Git ignores a hook without the executable bit. On Windows that bit lives in the index.
Get-ChildItem -Path (Join-Path $repoRoot '.githooks') -File |
    Where-Object { $_.Extension -ne '.md' } |
    ForEach-Object {
        $relative = ".githooks/$($_.Name)"
        git update-index --chmod=+x $relative 2>$null
    }

Write-Host 'Git hooks installed: core.hooksPath -> .githooks'
Write-Host ''
Write-Host '  pre-commit  secret scan, lint and build for the areas you touched'
Write-Host '  commit-msg  Conventional Commits'
Write-Host '  pre-push    backend and frontend test suites'
Write-Host ''
Write-Host 'Bypass any of them once with SKIP_HOOKS=1, e.g.  SKIP_HOOKS=1 git commit ...'
