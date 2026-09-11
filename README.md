# OpenBigData

This project is built with AI coding agents (Claude Code) using the [agent-skills](https://github.com/addyosmani/agent-skills) lifecycle: spec → plan → build → test → review → ship.

## Agent setup

The content of [addyosmani/agent-skills](https://github.com/addyosmani/agent-skills) (v0.6.9, MIT-licensed, see [.claude/agent-skills-LICENSE](.claude/agent-skills-LICENSE)) is vendored directly into this project's `.claude/` directory, since this Claude Code environment doesn't support the plugin/marketplace system:

- `.claude/skills/` — 25 lifecycle skills (spec, plan, build, test, review, ship, and more)
- `.claude/agents/` — 4 reviewer personas (`code-reviewer`, `security-auditor`, `test-engineer`, `web-performance-auditor`)
- `.claude/commands/` — 9 slash commands (`/spec`, `/plan`, `/build`, `/test`, `/constraints`, `/review`, `/code-simplify`, `/ship`, `/webperf`)
- `.claude/references/` — shared checklists the skills pull in (security, performance, accessibility, testing, observability, definition of done, orchestration patterns)
- `.claude/hooks/session-start.sh` — injects the skill-discovery meta-skill at the start of every session (wired via `.claude/settings.json`)

Anyone opening this repo in Claude Code gets this automatically — no install step, no plugin approval needed. To pull in upstream updates later, re-sync from the [source repo](https://github.com/addyosmani/agent-skills).

## Status

Just started. No code yet.
