## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (uses the `gh` CLI). See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context. **Start at `CONTEXT-INDEX.md`** — `CONTEXT.md` is ~122 KB and should be read by
line range, not whole. Decisions live in `docs/adr/` (start at its `README.md`); read those first.
See `docs/agents/domain.md`.

### `start-lane` is mirrored to Codex

`.claude/skills/start-lane/SKILL.md` is the source of truth. Codex reads its own copy at
`~/.codex/skills/start-lane/SKILL.md`, which differs by exactly one line — Claude's
`disable-model-invocation: true`, which Codex expresses as `policy.allow_implicit_invocation: false`
in its `agents/openai.yaml`. Nothing syncs them, so an unmirrored edit leaves Codex lanes running
the old rules. After editing the skill, in the same change:

```sh
grep -v '^disable-model-invocation: true$' .claude/skills/start-lane/SKILL.md \
  > ~/.codex/skills/start-lane/SKILL.md
```
