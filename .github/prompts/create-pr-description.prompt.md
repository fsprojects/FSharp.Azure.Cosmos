---
mode: agent
description: Generates a pull request description for FSharp.Azure.Cosmos by analyzing the current branch's changes and filling in the project's PR template.
---

# Create PR Description

## Context

#file:'.github/PULL_REQUEST_TEMPLATE.md'

> If the `#file:` reference above cannot be resolved, run `Get-Content .github/PULL_REQUEST_TEMPLATE.md` in PowerShell from the repository root to read the template.

## Task

Analyze the current branch's git diff and commit history relative to the base branch (`main` or `master`), then produce a fully filled-in PR description that conforms to the project's `PULL_REQUEST_TEMPLATE.md`.

## Steps

1. Run `git log` and `git diff` to understand what changed, which files were modified, and why.
2. Summarize the **big picture** of the changes — what problem is solved or what capability is added.
3. Identify the **type(s) of change** (bugfix / new feature / breaking change) based on the diff.
4. Evaluate the **checklist** items by inspecting the diff:
   - Check whether tests were added or modified.
   - Check whether documentation or XML doc comments were added or updated.
   - Assume build/tests pass if no obvious compilation errors are visible (note this assumption).
5. Write **Further comments** only if the change is large or architecturally significant; otherwise omit this section.

## Guidelines

- Be factual and precise — base every statement on the actual diff and commits, not assumptions.
- Keep the **Proposed Changes** section concise but informative (3–6 sentences max).
- For checklist items that cannot be determined from the diff alone, leave them unchecked and add a short inline note.
- Do not invent issue numbers or links unless they appear in commit messages or branch names.
- Use plain English; avoid vague filler phrases like "various improvements".
- Use only en dashes (`–`) for dashes; never use em dashes (`—`).
- Preserve all original template headings, checkbox syntax, and section order exactly.

## Output

A single fenced markdown code block containing the fully filled-out PR description, ready to copy and paste directly into GitHub. Do not escape any markdown syntax inside the block. Match the structure of `PULL_REQUEST_TEMPLATE.md` exactly:

- `## Proposed Changes` — narrative paragraph(s)
- `## Types of changes` — checkboxes with `x` placed in the correct box(es)
- `## Checklist` — checkboxes filled based on evidence from the diff
- `## Further comments` — include only when the change warrants extra explanation; omit the section entirely otherwise

Do not include any explanation or commentary outside the code block.
