# Agent rules (SphereEmu)

These rules apply to every AI agent working in this repository.

## Networking

- At the start of every conversation, read `.cursor/skills/sphere-networking-live/SKILL.md` and follow it.

## Comment style

- At the start of every conversation, read `.cursor/skills/code-comment-style/SKILL.md` and follow it for the rest of the session.
- It covers comments, docstrings, descriptions, log/echo/error lines, script output, help text, commit text, and chat replies. Replies combine this skill's wording with i-have-adhd shape.
- A `sessionStart` hook injects the same skill. Invoke again with `/code-comment-style`.

## Punctuation

- NEVER USE A FUCKING EM DASH YOU MORON (U+2014, `—`) ANYWHERE: UI strings, comments, commit messages, docs, or chat. Use a comma, colon, parentheses, or a regular hyphen-minus (`-`) instead.

## Comments

- Keep comments succinct and to the point.
- Aggressively prune redundant comments where the code already describes itself.
- Prefer explaining non-obvious intent, constraints, or tradeoffs, not restating the next few lines.
- Break comment lines at about 100 characters. A wrap is the same thought, not a new one.
- XML doc tags sit on their own lines (`<summary>`, then the text, then `</summary>`). Same for `<param>`, `<returns>`, `<remarks>`. Not `<summary>text</summary>`.

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
- Space before `(` on calls, declarations, primary constructors, and `new`: `Foo (x)`, `void Bar (int x)`, `class Baz (int x)`, `new Point (x, y)`.
- Space after a cast: `(int) value`, `(byte) (n >> 8)`.
- Control-flow keywords stay as written: `if (`, `for (`, `while (`, `switch (`, `catch (`, `foreach (`, `using (`, `lock (`.
