using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Điều khiển điều kiện Rune, hiệu ứng phát sáng và chuyển động bật dậy của một Seal.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class SealController : MonoBehaviour, IInteractable
{
    [Tooltip("Thùng gỗ phải bị sóng xung kích phá trước khi Seal có thể tương tác.")]
    [SerializeField] private RuneController _requiredRune;
    [Tooltip("Bán kính người chơi có thể tìm và tương tác với Seal.")]
    [SerializeField, Min(0.1f)] private float _interactionRadius = 1.2f;
    [Tooltip("Số giây Seal giữ trạng thái đã kích hoạt trước khi tự tắt và khôi phục thùng tương ứng.")]
    [SerializeField, Range(10f, 15f)] private float _activeDuration = 12f;
    [Tooltip("Tên pivot chứa model Seal và được dùng để chạy chuyển động bật dậy.")]
    [SerializeField] private string _visualPivotName = "Seal Visual Pivot";
    [Tooltip("Màu phát sáng của Seal sau khi thùng gỗ tương ứng bị phá.")]
    [SerializeField] private Color _readyEmissionColor = new(0.05f, 0.9f, 1f, 1f);
    [Tooltip("Cường độ phát sáng khi Seal đang chờ người chơi tương tác.")]
    [SerializeField, Min(0f)] private float _readyEmissionIntensity = 2.2f;
    [Tooltip("Cường độ phát sáng sau khi Seal đã được người chơi kích hoạt.")]
    [SerializeField, Min(0f)] private float _activeEmissionIntensity = 3f;
    [Tooltip("Cường độ đèn phụ khi Seal đang chờ tương tác.")]
    [SerializeField, Min(0f)] private float _readyLightIntensity = 1.5f;
    [Tooltip("Cường độ đèn phụ sau khi Seal đã bật dậy.")]
    [SerializeField, Min(0f)] private float _activeLightIntensity = 2.2f;
    [Tooltip("Phạm vi chiếu sáng của đèn phụ trên Seal.")]
    [SerializeField, Min(0.1f)] private float _stateLightRange = 3f;
    [Tooltip("Vị trí local cuối cùng của pivot khi Seal bật dậy.")]
    [SerializeField] private Vector3 _activeLocalPosition = new(0f, 1.2f, 0f);
    [Tooltip("Góc xoay local cuối cùng của pivot khi Seal bật dậy.")]
    [SerializeField] private Vector3 _activeLocalEulerAngles = new(-90f, 0f, 0f);
    [Tooltip("Thời gian Seal chuyển từ nằm trên sàn sang tư thế bật dậy.")]
    [SerializeField, Min(0.05f)] private float _poseTransitionDuration = 0.45f;

    private readonly List<Material> _stateMaterials = new();
    private SphereCollider _interactionTrigger;
    private SealManager _manager;
    private Transform _visualPivot;
    private Light _stateLight;
    private Vector3 _inactiveLocalPosition;
    private Quaternion _inactiveLocalRotation;
    private Coroutine _poseRoutine;
    private float _activeUntil;

    /// <summary>Trạng thái hiện tại của Seal.</summary>
    public SealState State { get; private set; } = SealState.Inactive;

    /// <summary>Thùng gỗ bắt buộc phải bị phá trước Seal này.</summary>
    public RuneController RequiredRune => _requiredRune;

    /// <inheritdoc />
    public string InteractionPrompt => $"Activate {name}";

    /// <inheritdoc />
    public bool CanInteract => State == SealState.Ready;

    /// <inheritdoc />
    public bool IsActivated => State == SealState.Active;

    /// <summary>Phát ra mỗi khi trạng thái Seal thay đổi.</summary>
    public event Action<SealController, SealState> StateChanged;

    private void Awake()
    {
        _interactionTrigger = GetComponent<SphereCollider>();
        _interactionTrigger.isTrigger = true;
        _interactionTrigger.radius = _interactionRadius;
        _manager = GetComponentInParent<SealManager>();
        CacheModelVisual();
        RefreshReadiness();
        ApplyVisualState(false);
    }

    private void OnDisable()
    {
        if (_poseRoutine == null) return;
        StopCoroutine(_poseRoutine);
        _poseRoutine = null;
    }

    private void OnDestroy()
    {
        foreach (Material material in _stateMaterials)
            if (material != null) Destroy(material);
        _stateMaterials.Clear();
    }

    private void Update()
    {
        if (!IsServerAuthority()) return;

        if (State == SealState.Active && Time.time >= _activeUntil)
            ResetAfterActiveTimeout();
        else if (State == SealState.Ready && (_requiredRune == null || _requiredRune.State != RuneState.Charged))
            SetState(SealState.Inactive);
        else if (State == SealState.Inactive)
            RefreshReadiness();
    }

    /// <inheritdoc />
    public void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            BossNetworkState.Instance?.RequestSealInteraction(this);
            return;
        }

        if (_manager == null) _manager = GetComponentInParent<SealManager>();
        _manager?.TryActivateSeal(this, playerId);
    }

    /// <inheritdoc />
    public void OnHoverEnter() { }

    /// <inheritdoc />
    public void OnHoverExit() { }

    /// <inheritdoc />
    public Transform GetPromptTransform() => _visualPivot != null ? _visualPivot : transform;

    /// <summary>Chuyển Seal sang phát sáng sau khi thùng gỗ tương ứng bị phá.</summary>
    public void RefreshReadiness()
    {
        if (State != SealState.Inactive || _requiredRune == null || _requiredRune.State != RuneState.Charged) return;
        SetState(SealState.Ready);
    }

    /// <summary>Kích hoạt Seal và chạy chuyển động bật dậy sau khi manager xác thực người chơi.</summary>
    public bool TryActivate()
    {
        if (State != SealState.Ready || _requiredRune == null || !_requiredRune.TryConsume()) return false;

        _activeUntil = Time.time + _activeDuration;
        SetState(SealState.Active);
        return true;
    }

    /// <summary>Đưa Seal về trạng thái ban đầu cho chu kỳ kế tiếp.</summary>
    public void ResetSealForCycle()
    {
        _activeUntil = 0f;
        if (State == SealState.Inactive) ApplyVisualState(true);
        else SetState(SealState.Inactive);
    }

    /// <summary>Áp dụng trạng thái Seal do Host đồng bộ sang Client.</summary>
    public void ApplyNetworkState(SealState state)
    {
        _activeUntil = 0f;
        if (State == state)
        {
            ApplyVisualState(false);
            return;
        }

        SetState(state);
    }

    private void ResetAfterActiveTimeout()
    {
        _activeUntil = 0f;
        _requiredRune?.RestoreInitialPosition();
        SetState(SealState.Inactive);
        Debug.Log($"[SealController] {name} hết thời gian kích hoạt và đã trở về trạng thái ban đầu.", this);
    }

    private void SetState(SealState nextState)
    {
        if (State == nextState) return;

        State = nextState;
        ApplyVisualState(false);
        Debug.Log($"[SealController] {name} chuyển sang trạng thái {State}.", this);
        StateChanged?.Invoke(this, State);
    }

    private void CacheModelVisual()
    {
        _visualPivot = transform.Find(_visualPivotName);
        if (_visualPivot == null)
        {
            Transform model = transform.Find("Seal_Model");
            if (model != null)
            {
                GameObject pivotObject = new(_visualPivotName);
                _visualPivot = pivotObject.transform;
                _visualPivot.SetParent(transform, false);
                model.SetParent(_visualPivot, false);
            }
        }

        if (_visualPivot == null)
        {
            Debug.LogError($"[SealController] Không tìm thấy model Seal cho {name}.", this);
            return;
        }

        _inactiveLocalPosition = _visualPivot.localPosition;
        _inactiveLocalRotation = _visualPivot.localRotation;
        CacheMaterialInstances();
        CreateStateLight();
    }

    private void CacheMaterialInstances()
    {
        _stateMaterials.Clear();
        foreach (Renderer renderer in _visualPivot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.materials;
            foreach (Material material in materials)
            {
                if (material != null && !_stateMaterials.Contains(material))
                    _stateMaterials.Add(material);
            }
        }
    }

    private void CreateStateLight()
    {
        Transform existingLight = _visualPivot.Find("Seal Activation Light");
        if (existingLight != null) _stateLight = existingLight.GetComponent<Light>();

        if (_stateLight == null)
        {
            GameObject lightObject = new("Seal Activation Light");
            lightObject.transform.SetParent(_visualPivot, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            _stateLight = lightObject.AddComponent<Light>();
            _stateLight.type = LightType.Point;
            _stateLight.shadows = LightShadows.None;
        }

        _stateLight.color = _readyEmissionColor;
        _stateLight.range = _stateLightRange;
    }

    private void ApplyVisualState(bool snapPose)
    {
        float emissionIntensity = State switch
        {
            SealState.Ready => _readyEmissionIntensity,
            SealState.Active => _activeEmissionIntensity,
            _ => 0f
        };
        Color emissionColor = _readyEmissionColor * emissionIntensity;

        foreach (Material material in _stateMaterials)
        {
            if (material == null || !IsGlowAccentMaterial(material) || !material.HasProperty("_EmissionColor"))
                continue;

            material.SetColor("_EmissionColor", emissionColor);
            if (emissionIntensity > 0f) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
        }

        if (_stateLight != null)
        {
            _stateLight.color = _readyEmissionColor;
            _stateLight.range = _stateLightRange;
            _stateLight.intensity = State switch
            {
                SealState.Ready => _readyLightIntensity,
                SealState.Active => _activeLightIntensity,
                _ => 0f
            };
            _stateLight.enabled = _stateLight.intensity > 0f;
        }

        SetRaisedPose(State == SealState.Active, snapPose);
    }

    private static bool IsGlowAccentMaterial(Material material)
    {
        string materialName = material.name.ToLowerInvariant();
        return materialName.Contains("cyan") ||
               materialName.Contains("turquoise") ||
               materialName.Contains("ivory") ||
               materialName.Contains("crystal") ||
               materialName.Contains("emission") ||
               materialName.Contains("glow");
    }

    private void SetRaisedPose(bool isRaised, bool snap)
    {
        if (_visualPivot == null) return;

        Vector3 targetPosition = isRaised ? _activeLocalPosition : _inactiveLocalPosition;
        Quaternion targetRotation = isRaised
            ? Quaternion.Euler(_activeLocalEulerAngles)
            : _inactiveLocalRotation;

        if (_poseRoutine != null)
        {
            StopCoroutine(_poseRoutine);
            _poseRoutine = null;
        }

        if (snap || !isActiveAndEnabled)
        {
            _visualPivot.localPosition = targetPosition;
            _visualPivot.localRotation = targetRotation;
            return;
        }

        _poseRoutine = StartCoroutine(AnimatePose(targetPosition, targetRotation));
    }

    private IEnumerator AnimatePose(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3 startPosition = _visualPivot.localPosition;
        Quaternion startRotation = _visualPivot.localRotation;
        float elapsed = 0f;

        while (elapsed < _poseTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / _poseTransitionDuration);
            _visualPivot.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, progress);
            _visualPivot.localRotation = Quaternion.SlerpUnclamped(startRotation, targetRotation, progress);
            yield return null;
        }

        _visualPivot.localPosition = targetPosition;
        _visualPivot.localRotation = targetRotation;
        _poseRoutine = null;
    }

    private static bool IsServerAuthority() =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
}
