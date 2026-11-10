Append every user message to `History.md`.
Give each message in `History.md` its own test file.
If a new message contradicts an earlier one, ask whether the behavior change is intended. If yes, add the new test, delete the old one, and mark the old entry `\[SUPERSEDED]`.
Ignore `\[SUPERSEDED]` entries when checking for contradictions.
If a test fails, don't fix it. Explain why it failed and ask whether that's intended.
If `History.md` exceeds 500 lines, move the oldest half to `History.archive.md`. Only read the archive when checking for contradictions.