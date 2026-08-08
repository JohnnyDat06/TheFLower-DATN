# Cat Sphinx Boss Development Rules

For any task related to the Map 2 Cat Sphinx Guardian boss:

1. MUST read:
   - docs/boss/Boss_Gameplay_Spec_Map2_Desert.md
   - docs/boss/Cat_Sphinx_Boss_Implementation_Plan.md

2. The Gameplay Spec is the source of truth for boss mechanics.

3. The Implementation Plan is the source of truth for development order.

4. MUST implement only ONE implementation phase at a time.

5. MUST NOT implement files or mechanics belonging to later phases.

6. After completing the current phase:
   - stop coding,
   - report files created/modified,
   - provide the Manual Test checklist for that phase,
   - wait for the user to confirm PASS.

7. If Manual Test FAILS:
   - only fix the current phase,
   - do not continue to the next phase.

8. Only proceed when the user explicitly confirms the current Manual Test Gate has PASSed.

9. Do not redesign or add boss mechanics that are not defined in the Gameplay Spec.

10. Do not implement the whole boss at once.