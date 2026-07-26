---
name: sync-main
description: Fetch origin and bring the current branch up to date with main before starting work, so you're not editing against a stale tree. Use at the start of a task, or when asked to pull/sync/rebase on main or "get up to date".
---

# Sync with main before working

Work here happens in per-task worktrees that are branched off `main` and can fall
behind while a session runs. Editing against a stale tree means re-solving problems
already merged (a whole feature can look "missing" when it only landed on `main`).
Run this **before touching any files**.

## 1. Fetch

```
git fetch origin
```

Only updates remote-tracking refs — nothing in the working tree moves yet.

## 2. See what's incoming

```
git log --oneline HEAD..origin/main
```

Empty output means you're already current — stop, there's nothing to do. Otherwise
those are the commits about to arrive; skim them, a couple may be the thing you were
about to build.

## 3. Bring the branch onto latest main

```
git rebase origin/main
```

A clean fast-forward when the branch has no local commits of its own; otherwise it
replays them on top of `main`. Either way the working tree ends up on top of the
latest `main`.

## If it conflicts

Stop. Report the conflicting files and **do not** `--force` or `rebase --abort`
silently — let the user decide. Resolve in the worktree, then `git rebase --continue`.

## Notes

- This is a worktree (`.claude/worktrees/…`); the branch is the worktree's own branch,
  not `main` itself. You never check out or commit onto `main` directly — see
  `CLAUDE.md` → *Git*.
- Both PowerShell and Bash are available and their syntaxes differ; these `git`
  commands are the same in either. Keep a whole multi-line command in one shell.
