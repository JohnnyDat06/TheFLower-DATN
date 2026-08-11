using UnityEngine;

/// <summary>Ends the Cat Sphinx encounter on Core Hit #3 and unlocks the authored arena exit.</summary>
public sealed class BossDefeatController : MonoBehaviour
{
    [Tooltip("Kich thuoc barrier tam thoi tai ExitDoor khi scene chua co model cua that.")]
    [SerializeField] private Vector3 _fallbackDoorSize = new(4f, 4f, 0.5f);
    [Tooltip("Do cao local cua barrier tam thoi so voi ExitDoor marker.")]
    [SerializeField] private Vector3 _fallbackDoorLocalOffset = new(0f, 2f, 0f);
    [Tooltip("Trang thai debug cua defeat sequence.")]
    [SerializeField] private bool _debugIsDefeated;

    private BossCoreController _coreController;
    private BossPhaseController _phaseController;
    private BossController _bossController;
    private BossAnimationController _animationController;
    private BossArenaReferences _arenaReferences;
    private GameObject _fallbackDoorBarrier;
    private bool _isExitDoorUnlocked;

    /// <summary>True after the final Core Hit permanently stops boss combat and unlocks the exit.</summary>
    public bool IsDefeated => _debugIsDefeated;

    /// <summary>True after the authoritative final Core Hit removes the exit blocker.</summary>
    public bool IsExitDoorUnlocked => _isExitDoorUnlocked;

    private void Awake()
    {
        CacheDependencies();
        CreateFallbackDoorBarrier();
    }

    private void OnEnable()
    {
        if (_coreController == null) _coreController = GetComponent<BossCoreController>();
        if (_coreController != null) _coreController.CoreHit += HandleCoreHit;
    }

    private void OnDisable()
    {
        if (_coreController != null) _coreController.CoreHit -= HandleCoreHit;
    }

    private void HandleCoreHit()
    {
        if (_debugIsDefeated || _phaseController == null ||
            _phaseController.CurrentPhase != BossCombatPhase.PhaseThree)
            return;

        DefeatBoss();
    }

    [ContextMenu("Debug/Force Core Hit #3 + Defeat")]
    private void ForceFinalDefeatForDebug()
    {
        if (_debugIsDefeated) return;

        CacheDependencies();
        _phaseController?.DebugSetFinalCoreHit();
        DefeatBoss();
    }

    /// <summary>Applies the Host-owned terminal boss and Exit Door state on Client.</summary>
    public void ApplyNetworkState(bool isDefeated, bool isExitDoorUnlocked)
    {
        if (isDefeated && !_debugIsDefeated) DefeatBoss();
        if (isExitDoorUnlocked && !_isExitDoorUnlocked) UnlockExitDoor();
    }

    private void DefeatBoss()
    {
        _debugIsDefeated = true;
        _bossController?.Defeat();
        _animationController?.SetDefeated();
        PowerDownBossEffects();
        DisableCombatControllers();
        UnlockExitDoor();
        Debug.Log("[BossDefeatController] Cat Sphinx defeated. Exit Door unlocked.", this);
    }

    private void DisableCombatControllers()
    {
        DisableIfPresent<BossPhaseController>();
        DisableIfPresent<BossAttackSequence>();
        DisableIfPresent<BossPawSlamAttack>();
        DisableIfPresent<BossTargetSlamAttack>();
        DisableIfPresent<BossDoublePawAttack>();
        DisableIfPresent<BossEarthquakeAttack>();
        DisableIfPresent<FloorPatternController>();
    }

    private void DisableIfPresent<T>() where T : Behaviour
    {
        T controller = GetComponent<T>();
        if (controller != null) controller.enabled = false;
    }

    private void UnlockExitDoor()
    {
        _isExitDoorUnlocked = true;
        if (_arenaReferences == null || _arenaReferences.ExitDoor == null) return;

        foreach (Collider doorCollider in _arenaReferences.ExitDoor.GetComponentsInChildren<Collider>(true))
            doorCollider.enabled = false;

        if (_fallbackDoorBarrier != null) _fallbackDoorBarrier.SetActive(false);
    }

    private void CreateFallbackDoorBarrier()
    {
        if (_arenaReferences == null || _arenaReferences.ExitDoor == null) return;
        if (_arenaReferences.ExitDoor.GetComponentInChildren<Renderer>(true) != null) return;

        _fallbackDoorBarrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _fallbackDoorBarrier.name = "Exit Door Locked Barrier";
        _fallbackDoorBarrier.transform.SetParent(_arenaReferences.ExitDoor, false);
        _fallbackDoorBarrier.transform.localPosition = _fallbackDoorLocalOffset;
        _fallbackDoorBarrier.transform.localScale = _fallbackDoorSize;

        Renderer barrierRenderer = _fallbackDoorBarrier.GetComponent<Renderer>();
        barrierRenderer.material.color = new Color(0.25f, 0.55f, 0.65f, 1f);
    }

    private void PowerDownBossEffects()
    {
        if (_arenaReferences == null) return;

        Transform bossModel = _arenaReferences.transform.Find("CatFinalBoss");
        if (bossModel == null) return;

        foreach (Renderer renderer in bossModel.GetComponentsInChildren<Renderer>(true))
        {
            Material material = renderer.material;
            string visualName = $"{renderer.name} {material.name}".ToLowerInvariant();
            if (!visualName.Contains("eye") && !visualName.Contains("rune")) continue;

            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.black);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.black);
        }
    }

    private void CacheDependencies()
    {
        _coreController = GetComponent<BossCoreController>();
        _phaseController = GetComponent<BossPhaseController>();
        _bossController = GetComponent<BossController>();
        _animationController = GetComponent<BossAnimationController>();
        _arenaReferences = GetComponent<BossArenaReferences>();
    }
}
