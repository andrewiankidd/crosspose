---
name: commit
description: Review staged changes and create conventional commits, splitting large changesets logically
---

Review all staged changes and create one or more conventional commits.

## Step 1 — Situational awareness

Run in parallel:
- `git status` — staged, unstaged, and untracked files
- `git diff --cached --stat` — what is staged and how large
- `git diff --stat` — what is unstaged
- `git log --oneline -10` — recent commit style reference
- `cat .gitignore` — to understand what should never be committed

## Step 2 — Sanity check staged vs unstaged

Before committing, flag anything that looks wrong:

**Potentially unintentionally staged:**
- Build artifacts (`bin/`, `obj/`, `*.dll`, `*.exe`, `*.pdb`)
- IDE/editor files (`.vs/`, `*.user`, `.idea/`)
- Secrets or credentials (`.env`, `*.pfx`, `*.key`, `appsettings.*.json` with passwords, `auth.json`)
- Generated files that belong in `.gitignore`
- Lock files or temp files (`.tmp-*`, `*.log`)
- Unrelated changes in files not touched by the current feature

**Potentially unintentionally unstaged:**
- New files that are clearly part of the current change (new class used by staged code, new test for staged feature, updated CLAUDE.md for staged feature)
- Modified files that are obvious companions to staged changes (e.g. interface change staged but implementation not)

If anything looks wrong, **ask the user** before proceeding. Don't guess — list the suspect files and ask whether to stage/unstage them.

## Step 3 — Plan the commits

Read the full diff (`git diff --cached`) to understand the changes.

This repo uses conventional commits with scopes matching the project structure:
- `feat(scope):` — new feature
- `fix(scope):` — bug fix
- `refactor(scope):` — restructuring without behaviour change
- `chore(scope):` — tooling, config, gitignore, skills, docs-only
- `test(scope):` — test changes only

**Scopes** (match to which projects/areas changed):
`core`, `gui`, `cli`, `doctor`, `dekompose`, `orchestrator`, `oci`, `sources`, `config`, `deployment`, `networking`

**Split into multiple commits when:**
- Changes span clearly separate concerns (e.g. a Core library change + a GUI feature built on it = 2 commits)
- A bug fix is mixed in with a feature
- Test changes can be separated from implementation
- Infrastructure/config changes are mixed with feature code

**Keep as one commit when:**
- The change is a single coherent unit (interface + implementation + tests all for the same thing)
- Splitting would leave the repo in a broken intermediate state

For each planned commit, note which files belong to it.

## Step 4 — Execute

For each planned commit:
1. `git reset HEAD` to unstage everything (if splitting)
2. `git add <specific files>` for this commit's files
3. `git commit -m "..."` with the conventional message

Commit message rules:
- Subject line: `type(scope): concise description` — imperative mood, no period, ≤72 chars
- If the subject needs more context, add a blank line then a body
- Do not use `-m` with multiline; use a heredoc: `git commit -m "$(cat <<'EOF' ... EOF)"`
- Co-author line: `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`

## Step 5 — Verify

Run `git log --oneline -5` and show the result so the user can confirm everything looks right.

## Rules

- Never use `--no-verify` or skip hooks
- Never amend published commits
- Never commit if the user hasn't confirmed on anything flagged in Step 2
- Never commit `.env`, secrets, credentials, or build artifacts
- If a pre-commit hook fails, fix the underlying issue — don't bypass it
