---
name: review-docs
description: Review the repo's markdown files for gaps vs actual codebase contents
---

Review the repo's markdown files for gaps compared to the repo's current contents. Factor in all changes since the last root CLAUDE.md change.

Files to review:
- `.context/` — LLM/tech reference content
- `docs/` — human-facing documentation
- `CLAUDE.md` per project — LLM/tech content
- `README.md` per project — human-facing
- Root `CLAUDE.md` — LLM/tech content
- Root `README.md` — human-facing

Process:
1. Check git log to find the date of the last root CLAUDE.md change
2. Get all commits since then to understand what has changed in the codebase
3. Read each of the markdown files listed above
4. For each file, identify gaps: missing features, stale descriptions, undocumented patterns, or incorrect information relative to current code
5. Use the existing style of each file as reference — don't rewrite or overhaul, only fill genuine gaps
6. Make targeted edits only where there are real gaps. Skip files that are accurate and complete.
