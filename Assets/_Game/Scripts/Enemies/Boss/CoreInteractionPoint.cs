using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Điểm Core hạ từ trên cao và chờ một trong hai người chơi tương tác.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class CoreInteractionPoint : MonoBehaviour, IInteractable
{
    [Tooltip("Định danh của điểm Core này.")]
    [SerializeField] private CorePointId _pointId;
    [Tooltip("Bán kính người chơi có thể tìm và tương tác với Core.")]
    [SerializeField, Min(0.1f)] private float _interactionRadius = 1.2f;
    [Tooltip("Controller kiểm tra hai người chơi kích hoạt Core trong khoảng thời gian đồng bộ.")]
    [SerializeField] private DualCoreInteractionController _dualController;
    [Tooltip("Tên object model Core nằm dưới điểm tương tác trong Hierarchy.")]
    [SerializeField] private string _coreVisualName = "Core_Model";
    [Tooltip("Độ cao Core bắt đầu rơi xuống so với vị trí đã đặt trong scene.")]
    [SerializeField, Min(0.1f)] private float _descentHeight = 8f;
    [Tooltip("Thời gian Core hạ từ trên cao xuống vị trí đã đặt.")]
    [SerializeField, Min(0.1f)] private float _descentDuration = 1.4f;

    private SphereCollider _interactionTrigger;
    private GameObject _coreVisual;
    private Vector3 _targetLocalPosition;
    private Coroutine _descentRoutine;
    private bool _wasCoreExposed;

    /// <summary>Định danh ổn định dùng khi kiểm tra hai lượt tương tác.</summary>
    public CorePointId PointId => _pointId;

    /// <summary>Khoảng cách tối đa Host chấp nhận cho yêu cầu tương tác từ Client.</summary>
    public float ServerInteractionDistance => _interactionRadius + 2.2f;

    /// <summary>Thời gian model Core cần để hạ xuống vị trí tương tác.</summary>
    public float DescentDuration => _descentDuration;

    /// <summary>Chỉ đúng sau khi model Core đã hạ xuống tới vị trí được đặt.</summary>
    public bool IsReadyForInteraction { get; private set; }

    /// <inheritdoc />
    public string InteractionPrompt => $"Activate Core Point {_pointId}";

    /// <inheritdoc />
    public bool CanInteract =>
        IsReadyForInteraction &&
        _dualController != null &&
        _dualController.CanActivatePoint(this);

    /// <inheritdoc />
    public bool IsActivated => _dualController != null && _dualController.IsPointPending(this);

    private void Awake()
    {
        _interactionTrigger = GetComponent<SphereCollider>();
        _interactionTrigger.isTrigger = true;
        _interactionTrigger.radius = _interactionRadius;
        if (_dualController == null) _dualController = GetComponentInParent<DualCoreInteractionController>();
        CacheCoreVisual();
        HideCoreVisual();
    }

    private void OnDisable()
    {
        StopDescent();
    }

    private void Update()
    {
        if (_dualController == null) _dualController = GetComponentInParent<DualCoreInteractionController>();
        bool isCoreExposed = _dualController != null && _dualController.IsCoreExposed;

        if (isCoreExposed && !_wasCoreExposed)
            BeginDescent();
        else if (!isCoreExposed && _wasCoreExposed)
            HideCoreVisual();

        _wasCoreExposed = isCoreExposed;
    }

    /// <inheritdoc />
    public void Interact(ulong playerId)
    {
        if (!CanInteract) return;
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            BossNetworkState.Instance?.RequestCoreInteraction(this);
            return;
        }

        _dualController?.TryActivatePoint(this, playerId);
    }

    /// <inheritdoc />
    public void OnHoverEnter() { }

    /// <inheritdoc />
    public void OnHoverExit() { }

    /// <inheritdoc />
    public Transform GetPromptTransform() => _coreVisual != null ? _coreVisual.transform : transform;

    private void CacheCoreVisual()
    {
        Transform visual = transform.Find(_coreVisualName);
        if (visual == null)
        {
            Renderer renderer = GetComponentInChildren<Renderer>(true);
            visual = renderer != null ? renderer.transform : null;
        }

        _coreVisual = visual != null ? visual.gameObject : null;
        if (_coreVisual == null)
        {
            Debug.LogError($"[CoreInteractionPoint] Không tìm thấy model Core '{_coreVisualName}' cho {name}.", this);
            return;
        }

        _targetLocalPosition = _coreVisual.transform.localPosition;
    }

    private void BeginDescent()
    {
        if (_coreVisual == null) CacheCoreVisual();
        if (_coreVisual == null) return;

        StopDescent();
        IsReadyForInteraction = false;
        _coreVisual.SetActive(true);
        _coreVisual.transform.localPosition = _targetLocalPosition + Vector3.up * _descentHeight;
        _descentRoutine = StartCoroutine(AnimateDescent());
    }

    private IEnumerator AnimateDescent()
    {
        Vector3 startPosition = _coreVisual.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < _descentDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / _descentDuration);
            _coreVisual.transform.localPosition =
                Vector3.LerpUnclamped(startPosition, _targetLocalPosition, progress);
            yield return null;
        }

        _coreVisual.transform.localPosition = _targetLocalPosition;
        IsReadyForInteraction = true;
        _descentRoutine = null;
    }

    private void HideCoreVisual()
    {
        StopDescent();
        IsReadyForInteraction = false;
        if (_coreVisual == null) CacheCoreVisual();
        if (_coreVisual == null) return;

        _coreVisual.transform.localPosition = _targetLocalPosition;
        _coreVisual.SetActive(false);
    }

    private void StopDescent()
    {
        if (_descentRoutine == null) return;
        StopCoroutine(_descentRoutine);
        _descentRoutine = null;
    }
}

/// <summary>Định danh hai điểm Core được đặt trong arena.</summary>
public enum CorePointId
{
    A,
    B
}
