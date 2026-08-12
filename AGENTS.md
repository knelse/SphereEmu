# Agent rules (SphereEmu)

These rules apply to every AI agent working in this repository.

## Comments

- Keep comments succinct and to the point.
- Aggressively prune redundant comments where the code already describes itself.
- Prefer explaining non-obvious intent, constraints, or tradeoffs — not restating the next few lines.

## Commits

- Commit messages describe **why** the change exists (motivation, constraint, tradeoff), not an inventory of what files or lines changed.
- Do not commit unless the user explicitly asks.

## Build verification

- Before reporting work as done, run `dotnet build` and fix all errors.
- Repeat build → fix until the build succeeds.
- Skip this only when the user explicitly says to skip the build.

## Coding style

- Match the style of the surrounding code and file; do not apply generic conventions by default.
- Follow existing naming in the area you edit (this codebase generally avoids underscore-prefixed variables/fields).
- Prefer the local patterns for formatting, organization, and APIs over “clean code” defaults from elsewhere.
