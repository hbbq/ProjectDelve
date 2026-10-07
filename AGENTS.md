# AGENTS.md

This file gives coding agents repository-level working instructions for Project Delve.

Project Delve is a physical-first board game. The C#/.NET implementation, browser client, tools, and tests exist to implement and exercise that game; they do not define the game rules.

## Rules authority

`CONSTRAINTS.md` is the source of truth for established game rules and design constraints.

Before changing rules-related code or content, read the relevant parts of `CONSTRAINTS.md`. Align implementation, tests, terminology, and canonical Ability text with it.

Do **not** modify `CONSTRAINTS.md` unless the task explicitly asks for a rules/specification change. An implementation task is not permission to rewrite the rules to fit the implementation.

If implementation exposes an ambiguity, contradiction, missing rule, missing term, or a case where the requested mechanic cannot be implemented without making a new design decision:

1. Do not silently choose a rule.
2. Do not encode the missing decision as an implementation detail.
3. Do not edit `CONSTRAINTS.md` to resolve it.
4. Report the concrete rules question and the smallest relevant alternatives, then stop that part of the work until the rule is decided.

## Rules language and new mechanics

Use the established vocabulary and resolution semantics in `CONSTRAINTS.md` consistently. In particular, defined words such as `Attack`, `defeated`, `When`, `After`, `instead`, `choose`, `target`, `adjacent`, `bordering`, Posture, Side, and controller/agency distinctions have rules meaning.

New mechanics may require new rules vocabulary. Do not avoid a useful concrete mechanic merely because the current engine cannot express it, but do not invent terminology or semantics casually either. If a new term or rule is genuinely required, identify that requirement explicitly so the rules can be decided first.

Prefer implementing the smallest concrete mechanic required by current content. Do not introduce a generic effect system, rules DSL, trigger framework, status framework, modifier framework, or other speculative abstraction solely because future content might use it. Generalize only when existing concrete mechanics demonstrate a shared concept.

Canonical Ability text should describe the physical game naturally and precisely. Code implements that text and the shared rules; the text is not a programming language and must not be parsed as one.

## Physical-first boundary

Every game mechanic must remain executable and representable in the physical game.

Do not introduce hidden persistent state, computer-only timing semantics, UI-dependent rules, or remembered information that survives a stable boundary unless `CONSTRAINTS.md` explicitly establishes a physical representation for it.

Short-lived resolution context is acceptable where the rules explicitly require it. Keep such context scoped to the resolving sequence rather than turning it into persistent game state.

## Architecture boundaries

Rules/Game State is authoritative. Clients render state, present authoritative choices, submit decisions, and play back rules/domain events; they do not recreate game rules.

Side describes relationships. Controller/agency describes who makes decisions. Unit Type, Hero/Monster theme, and automated Behavior must not silently create separate legality or activation rules.

Behavior chooses among legal candidates; it does not define legality.

Prefer shared rules queries and concrete reusable mechanics over Unit-Type checks or presentation special cases.

## Changes and tests

When changing an established mechanic, inspect nearby content and tests for assumptions that may also need to change. Add or update focused tests for the rule being implemented, including meaningful interaction cases where existing mechanics meet the new one.

Run the relevant focused tests first, then the broadest practical existing verification for the affected projects. Do not weaken, delete, or rewrite unrelated tests merely to make a change pass.

If a required verification cannot be run in the current environment, state exactly what was attempted, what prevented it, and what remains unverified.

## Repository working knowledge

Agents may update **this section** of `AGENTS.md` when they discover stable, verified repository knowledge that would otherwise need to be rediscovered in future sessions.

Good additions include:

- exact commands that are reliably needed to build or test a project,
- prerequisites or environment setup actually required by repository tooling,
- non-obvious locations of important generated/test assets,
- recurring platform-specific pitfalls with a verified workaround,
- reliable debugging or verification procedures specific to this repository.

Do not use this section as a session log, scratchpad, changelog, issue tracker, or place for speculative advice. Do not record temporary failures, one-off machine state, secrets, credentials, personal paths, or facts that have not been verified.

Keep entries concise and current. If later work proves an entry wrong or obsolete, correct or remove it.

Agents may maintain this repository-working-knowledge section without separate permission. That permission does **not** extend to `CONSTRAINTS.md`, design decisions, gameplay rules, product scope, or other authoritative project documentation.

### Known repository notes

- The solution targets .NET 10. Run all C# tests with `dotnet test ProjectDelve.sln`; focused engine tests can use `dotnet test ProjectDelve.Engine.Tests --filter "FullyQualifiedName~TestClassName"`.
- Browser interaction and scenario-designer tests use Node's built-in runner: `node --test ProjectDelve.Web.Tests/*.test.cjs`. They run without a browser or npm dependency installation.
