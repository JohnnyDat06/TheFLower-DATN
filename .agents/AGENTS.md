# Agent Engineering Rules

These rules are mandatory for every feature, bug fix, refactor, and UI change.

## OOP and SOLID

### Single Responsibility

- Keep each class focused on one reason to change.
- Controllers coordinate UI and services; services own use cases; persistence adapters own storage.
- `InputDeviceDetector` detects devices only.
- `InputRebindService` owns rebind validation and conflict handling only.
- `InputSettingsPanelController` coordinates UI state only.
- `RebindRowController` owns one binding row only.

### Open/Closed

- Prefer new implementations, data entries, and strategies over large conditional chains.
- Add new controller types through `InputIconMap` data rather than changing display logic.
- Add persistence providers through `IInputBindingPersistence` rather than changing rebind rules.

### Liskov Substitution

- Every implementation must honor the contract, lifecycle, null policy, and error behavior of its interface.
- Replacing PlayerPrefs persistence with file or cloud persistence must not change the behavior expected by the caller.

### Interface Segregation

- Keep interfaces small and client-focused.
- Do not force a UI consumer to depend on persistence or device internals.
- Do not create a monolithic `InputManager` interface for unrelated responsibilities.

### Dependency Inversion

- Depend on abstractions at module boundaries.
- Use interfaces such as `IInputRebindService` and `IInputBindingPersistence` where the project already provides them.
- Inject Unity dependencies through serialized references or explicit constructors where practical.

## Clean Code

- Use clear English names: PascalCase for types, methods, and public properties; camelCase for locals; `_camelCase` for private fields; `UPPER_SNAKE_CASE` for constants.
- Keep methods short, deterministic, and easy to test. Prefer guard clauses over deep nesting.
- Avoid magic strings, duplicated business rules, unnecessary static state, hidden global state, and unused fields or callbacks.
- Keep domain logic independent from UI Toolkit, PlayerPrefs, networking, and input polling whenever practical.
- Keep UXML structural and USS responsible for styling. Avoid inline styles unless a runtime value requires them.
- Add comments only for non-obvious decisions or constraints.
- Preserve public contracts and existing architecture. Avoid unrelated refactors.
- Before finishing, remove dead code and check null references, event leaks, focus gaps, input locks, cursor state, and layout overlap.

## Input System Rules

- Read input through `PlayerInputHandler` and `InputDeviceDetector`; do not poll the Input System directly from unrelated gameplay classes.
- Use the control scheme groups `KeyboardMouse` and `Gamepad`.
- Use generic binding paths such as `<Gamepad>` rather than device-specific paths.
- Use `EventBus` for cross-system notifications and unsubscribe in `OnDisable` or the matching network lifecycle method.
- Check `IsOwner`, `IsServer`, and `IsClient` before network-sensitive logic.
- Null-check serialized dependencies in `Awake` and log actionable errors.
- Provide graceful fallbacks when a device detector, icon map, or binding entry is unavailable.

## UI Toolkit Rules

- UXML contains structure, USS contains styling, and C# controllers bind data and events.
- Query elements by their `name`, not by a styling class.
- Every interactive element must be focusable and reachable by keyboard and gamepad navigation.
- Verify normal, hover, focus, disabled, rebinding, modal, and error states.
- Ensure text has sufficient contrast against its background and never overlaps neighboring content.
- Fixed-format elements must use stable dimensions and clipping so images, labels, and buttons cannot resize the layout.
- Unsubscribe every registered callback when the owning UI document is disabled.

## Change Workflow

1. Inspect the current architecture, related assets, tests, `CONTRIBUTING.md`, and existing user changes before editing.
2. Define the smallest coherent change and identify the correct ownership boundary.
3. Implement using the existing project patterns and SOLID boundaries.
4. Validate proportionally: parse UXML/USS, compile changed C# scripts, inspect the Unity Console, and run focused tests or Play Mode checks when available.
5. Review the diff for unrelated changes, dead code, warnings, broken references, missing `.meta` files, and regression risk.
6. When the feature or bug fix is complete, create a compliant branch and focused commit. Do not leave completed work only in the working tree.

## Git Branch and Commit Rules

Follow `CONTRIBUTING.md` exactly:

- Branch names must be English and use `type/short-description`.
- Use `feat/` for features, `bugfix/` for bugs, `hotfix/` for urgent fixes, `refactor/` for restructuring, and `docs/` for documentation.
- Start feature branches from `main` unless the user explicitly requests another base or an existing work branch must be preserved.
- Commit messages must be English Conventional Commits with an imperative summary under 72 characters.
- Examples: `feat: add gamepad tab navigation`, `fix: prevent pause menu overlap`, `docs: update agent rules`.
- Stage only files belonging to the completed task. Never include unrelated user changes.
- Make one focused commit when a requested feature or fix is complete; split genuinely separate completed parts.
- Never reset, discard, rewrite, or revert user changes without explicit permission.
- Before reporting completion, verify branch, commit, and worktree state. Mention preserved unrelated changes and unavailable validation.

## Unity and MCP Validation

- After creating or editing scripts through Unity MCP, wait for compilation and read the Unity Console for errors and warnings.
- For UI changes, inspect the visual tree and capture a screenshot when the Unity connection is available.
- Confirm asset references and `.meta` files after adding or moving assets.
- Do not claim Play Mode validation when Unity MCP or the Editor is unavailable.

## Completion Checklist

- [ ] Responsibility boundaries and dependencies are clear.
- [ ] No duplicate logic, dead callbacks, unused UI nodes, or magic values were introduced.
- [ ] Keyboard, mouse, and gamepad paths were considered for interactive UI.
- [ ] UXML, USS, scripts, assets, and `.meta` files are valid and referenced correctly.
- [ ] Focus, visibility, cursor, input lock, and event unsubscribe behavior were checked.
- [ ] Compilation, Console, tests, and Play Mode checks were run when available.
- [ ] A compliant branch and focused Conventional Commit were created.
