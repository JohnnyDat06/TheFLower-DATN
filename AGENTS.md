# Sand Boat Chase Development Rules

For any task related to the Desert Sand Boat Chase:

1. MUST read:
   - docs/sand_boat/Dac_Ta_Gameplay_Sand_Boat_Chase_VI.md
   - docs/sand_boat/Ke_Hoach_Trien_Khai_Sand_Boat_Chase_VI.md

2. The Gameplay Spec is the source of truth for Sand Boat Chase mechanics.

3. The Implementation Plan is the source of truth for development order.

4. MUST implement only ONE implementation phase at a time.

5. MUST NOT implement files, systems, or mechanics belonging to later phases.

6. Before modifying code:
   - inspect the existing Unity project architecture,
   - reuse existing Player, Input, Networking, Camera, Checkpoint, UI, Audio, and Event systems when possible,
   - do not create duplicate systems unnecessarily.

7. Core gameplay rules MUST NOT be changed without explicit user approval:
   - P1 controls steering only,
   - P1 uses inverted controls: A = RIGHT, D = LEFT,
   - P2 controls speed only,
   - P2 uses W = accelerate, S = brake,
   - the Sand Boat automatically follows the predefined Route / Spline,
   - the boat cannot reverse,
   - the sandstorm pressure prevents players from staying at minimum speed indefinitely.

8. Sand Burst, Jump, Drift, Combat, procedural obstacles, alternate routes, or other additional mechanics MUST NOT be implemented unless explicitly requested.

9. After completing the current phase:
   - compile the Unity project,
   - stop coding,
   - report files created/modified,
   - provide the Manual Test checklist for that phase,
   - wait for the user to confirm PASS.

10. If Manual Test FAILS:
    - only fix the current phase,
    - do not continue to the next phase.

11. Only proceed when the user explicitly confirms the current Manual Test Gate has PASSed.

12. Do not redesign or add Sand Boat Chase mechanics that are not defined in the Gameplay Spec.

13. Do not modify the Cat Sphinx boss gameplay while implementing Sand Boat Chase unless the current integration phase explicitly requires it.

14. For multiplayer:
    - follow the project's existing networking architecture,
    - do not replace the networking framework,
    - avoid duplicate collision, fail, reset, finish, or boss-activation events,
    - keep one authoritative Sand Boat gameplay state.

15. Do not implement the whole Sand Boat Chase at once.

Core directive:

DO NOT IMPLEMENT THE WHOLE SAND BOAT CHASE AT ONCE.
IMPLEMENT ONE PHASE.
STOP.
MANUAL TEST.
WAIT FOR PASS.
THEN CONTINUE.
