---
name: datn-coop-development
description: Mandatory DATN Co-op development standards for maintainable Unity C# code. Use for every project code, prefab, scene, network, UI, test, branch, commit, or refactor task.
---

# DATN Co-op Development Standards

Read this file and the repository [CONTRIBUTING.md](../../../CONTRIBUTING.md) before changing the project. Treat these rules as mandatory for every task.

## Architecture and OOP

- Keep each class focused on one responsibility. Split orchestration, domain rules, persistence, networking, presentation, and input when they change for different reasons.
- Prefer composition and small interfaces over inheritance-heavy or God-MonoBehaviour designs.
- Encapsulate mutable state. Expose read-only properties or intention-revealing methods instead of public fields.
- Keep domain logic testable and independent from Unity APIs where practical; isolate `MonoBehaviour`, `NetworkBehaviour`, scene lookup, and UI glue at boundaries.
- Use dependency inversion for services and external systems. Inject or assign collaborators through interfaces, serialized references, or explicit setup methods.
- Use ScriptableObjects for shared immutable/configuration data; do not use them as hidden runtime global state.

## SOLID and clean code

- Single Responsibility: one reason to change per class and method.
- Open/Closed: extend through interfaces, strategies, events, or data instead of editing large conditionals.
- Liskov Substitution: derived components must preserve base contracts and lifecycle assumptions.
- Interface Segregation: keep interfaces small and capability-specific.
- Dependency Inversion: high-level gameplay systems must not depend directly on concrete UI, transport, or persistence implementations.
- Use clear PascalCase for types/methods/properties, `_camelCase` for private/protected fields, and camelCase for locals/parameters.
- Prefer guard clauses, explicit names, small methods, immutable inputs, and early validation. Remove dead code and duplicated literals.
- Avoid per-frame allocations, repeated scene searches, hidden singletons, magic numbers, and unnecessary static mutable state.
- Add concise XML comments for public APIs and explain non-obvious network or lifecycle decisions.

## Unity and NGO

- Follow the project prefab/core rules in `CONTRIBUTING.md`; update source prefabs instead of scene overrides for shared systems.
- Keep `Awake`, `OnEnable`, `Start`, `OnNetworkSpawn`, and `OnNetworkDespawn` responsibilities explicit and symmetric. Unsubscribe every event that is subscribed.
- Server-authoritative state must be validated on the server. Clients may request actions but must not write authoritative state.
- Check `IsServer`, `IsClient`, `IsOwner`, and spawn state before network logic. Validate sender identity, distance, permissions, and object references in RPC handlers.
- Use `EventBus` for cross-module signals, not as a replacement for ownership, state storage, or direct local collaboration.
- Preserve `.meta` files and stable prefab/scene references. Do not edit generated or third-party assets unless the task explicitly requires it.

## Required workflow

1. Inspect the relevant scene, prefab, scripts, packages, and `CONTRIBUTING.md` before editing.
2. Make the smallest cohesive change. Keep APIs explicit and avoid unrelated cleanup.
3. Validate scripts and compile in Unity. Read the Unity console and fix every new error; distinguish pre-existing third-party warnings from regressions.
4. Run relevant EditMode/PlayMode tests. For scene/prefab/UI changes, load the target scene, exercise the affected path, and capture or inspect the resulting layout when possible.
5. Run `git status`, review the diff, and ensure unrelated user changes are not staged.
6. Create a branch from the current base using the English `type/short-description` format required by `CONTRIBUTING.md`.
7. Commit only after tests pass, using an English Conventional Commit under 72 characters.
8. Report tests, known pre-existing issues, branch, commit, and any remaining uncommitted user changes.

## Git safety

Before Git mutations, apply the `git-lock-recovery` workflow. If `.git/index.lock` exists, inspect Git/Git LFS processes first; delete only a verified stale lock. Never discard, reset, clean, or overwrite user changes as part of lock recovery without explicit scope.
