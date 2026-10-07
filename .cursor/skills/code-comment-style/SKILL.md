---
name: code-comment-style
description: 'Write generated text short, about the item it sits on, in the file''s own wording. Covers comments, docstrings, descriptions, log/echo/error lines, script output, help text, commit text, and chat replies. Chat replies combine this wording with i-have-adhd shape. Loaded at session start by a hook and stays on all session. Invoke with /code-comment-style.'
disable-model-invocation: true
metadata:
  tags: "Comments, Docstrings, Style, Code"
  category: "coding"
---

# code-comment-style

Generated text reads like the file's existing text. These rules apply to every piece of text you add or edit in a file, for the rest of the session: comments, docstrings, module and header blocks, descriptions (YAML/CI `description:`, argparse help, config), log, echo and error lines, script output, and commit/MR text. Languages: code, YAML/CI, shell, config. Chat replies use this wording plus i-have-adhd shape; see Chat replies.

## Rules

- Imitate first. Read the two or three nearest comments, docstrings or messages in the file and match their length, wording and punctuation. If this skill and the file disagree, the file wins, except the tag layout and the 100-character break below.
- Scope to the item it sits on (the line below a comment, the function a docstring belongs to, the step a log line reports). Say why that item is the way it is. Don't explain how other lists, jobs or functions relate; the code shows it.
- One thought by default. A second thought only if the first can't carry the reason. A docstring is one thought unless the signature can't carry the contract; a module docstring is a summary line plus Usage/Requires if the file already uses them.
- Break a comment or docstring line at about 100 characters. The next line is the same thought, same marker (`//`, `///`, `#`), same indent. Don't break commented-out code or a byte dump.
- An XML doc tag is never one line. Opening tag, text, closing tag, for `<summary>`, `<param>`, `<returns>`, and `<remarks>`. Not `<summary>text</summary>`.
- State the fact rather than an instruction when you can: "Kept in sync with X via lint job", not "Keep in sync with X".
- Use the team's words and contractions: "envs", "Contrib", acronyms as they write them.
- End on the concrete value ("so this is only research"), not a restatement of the name.
- No final period on one thought, including when that thought wraps at 100 characters. A second thought ends with one period.
- Leave out what the code does, history ("now", "previously"), refactor notes, ticket numbers, and why the file is laid out the way it is.
- Log, echo and error lines: say what happened and the value, no narration ("Skipping X: not deployed in region", not "Now we are going to skip X because it is not deployed"). Keep any existing prefixes (`SKIP:`, `Resolved:`) and don't add emojis or banners.

## Chat replies

Replies use both this skill and i-have-adhd (`~/.cursor/skills/i-have-adhd/SKILL.md`). Read that skill if it is not already in context.

Shape from i-have-adhd. Wording from this skill.

- First line is the answer or the next action. If work remains, the last line is one next action the reader can do in under two minutes.
- Number the steps when there is more than one. Each step is one action. Cap the list at 5.
- On a multi-step task, restate the step: "Step 2 of 4 done: schema updated. Next: backfill the column."
- One line when one fact answers it. A second line only if the first can't carry the result.
- About this turn's item. Cut history, side issues, and a recap of the edit.
- State the result ("Login works with magic links. Try `npm run dev`, open `/login`."), not a tour of the edit.
- End on the concrete value: path, command, or number.
- Time in concrete units when the reader still has work ("about 15 minutes if tests cover this").
- Errors: cause, then fix.

File rules stay on file text. "State the fact rather than an instruction" and "no final period on a one-liner" stay off chat. i-have-adhd picks the shape (action first, numbered steps, one next action). This skill picks the wording (short, this item, concrete value).

If the reader says "stop adhd mode" or "normal mode", drop the i-have-adhd shape for this chat. This skill's wording stays. Confirm in one line.

## Editing existing text

- Start from the text as it is now. The user edits between turns.
- Keep wording the user wrote. Change only what was asked.
- To shorten, cut sentences instead of rewording the rest. Say what you dropped.

## Examples

Too long (3 lines, about other lists):

```
Contributor tests run in every envs_1 and envs_2 environment, so the
tested presets reuse those lists. Only the region-only production list needs its own
tested variant, because it holds envs_3 environments, which the tests skip.
```

In style (1 line, about the item below):

```
Contrib tests don't run in envs_3, so this is only envs_1
```

Instruction turned into a fact:

```
Keep in sync with ExampleMethod() in file.cs
Kept in sync with ExampleMethod() in file.cs via lint job
```

Docstring that restates the code:

```
"""Return the regions with a values file for env, sorted by name, by globbing values/."""
"""Return the regions with a values file for env."""
```

Collapsed tag, and a line past 100:

```
/// <summary>Quarter 0-1, minute 2-7, hour 8-12, day 13-17, month 18-21, wire year 22-31; HUD year is wire + 7800</summary>
```

```
/// <summary>
/// Quarter 0-1, minute 2-7, hour 8-12, day 13-17, month 18-21, wire year
/// 22-31; HUD year is wire + 7800
/// </summary>
```

## Before sending

For each piece of file text you added: is it about the item it sits on? Can a sentence go? Does it sound like its neighbours? Is every line about 100 characters or under, and is every doc tag on its own line? If it is off the item, or a sentence can go, cut it. If a line is past 100 or a tag is inline, break it.

For a chat reply: first line is the answer or the action, last line is the one next step or the result. If a sentence restates the question or narrates the edit, cut it.
