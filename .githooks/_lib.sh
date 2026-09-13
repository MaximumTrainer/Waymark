#!/bin/sh
# Shared helpers for the repository's git hooks.
#
# Sourced by every hook in this directory. POSIX sh: Git for Windows runs hooks under its bundled
# sh, so bashisms are avoided.

REPO_ROOT=$(git rev-parse --show-toplevel)
export REPO_ROOT

# Colour only when attached to a terminal, so hook output stays readable when piped or logged.
if [ -t 1 ]; then
    C_RED=$(printf '\033[31m')
    C_GREEN=$(printf '\033[32m')
    C_YELLOW=$(printf '\033[33m')
    C_DIM=$(printf '\033[2m')
    C_OFF=$(printf '\033[0m')
else
    C_RED='' C_GREEN='' C_YELLOW='' C_DIM='' C_OFF=''
fi

hook_info()  { printf '%s\n' "${C_DIM}[hook] $1${C_OFF}"; }
hook_ok()    { printf '%s\n' "${C_GREEN}[hook] $1${C_OFF}"; }
hook_warn()  { printf '%s\n' "${C_YELLOW}[hook] $1${C_OFF}"; }
hook_error() { printf '%s\n' "${C_RED}[hook] $1${C_OFF}" >&2; }

# Every hook honours SKIP_HOOKS=1, which is friendlier than --no-verify because it is visible in
# the command that used it.
hooks_skipped() {
    if [ "${SKIP_HOOKS:-0}" = "1" ]; then
        hook_warn "SKIP_HOOKS=1 set - skipping $1."
        return 0
    fi
    return 1
}

# Files staged for commit, added/copied/modified/renamed only: a path being deleted must not
# trigger a build of the area it used to live in.
staged_files() {
    git diff --cached --name-only --diff-filter=ACMR
}

# True when any of the given paths match the supplied prefix pattern.
paths_touch() {
    pattern=$1
    shift
    printf '%s\n' "$@" | grep -Eq "$pattern"
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        hook_error "'$1' is not on PATH; cannot run $2."
        hook_error "Install it, or bypass this once with SKIP_HOOKS=1."
        return 1
    fi
    return 0
}

# Runs a command from the repository root, printing it first and failing loudly.
run_step() {
    label=$1
    shift
    hook_info "$label"
    if ! ( cd "$REPO_ROOT" && "$@" ); then
        hook_error "$label FAILED"
        return 1
    fi
    return 0
}
