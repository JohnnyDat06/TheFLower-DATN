using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// CameraFocusSequence - Thực hiện việc tập trung camera vào một điểm chỉ định trong một khoảng thời gian.
/// Thường dùng khi hoàn thành nhiệm vụ hoặc mở cửa.
/// </summary>
public class CameraFocusSequence : MonoBehaviour
{
    private const int InactivePriority = 0;
    private const int MinimumFocusPriority = 21;

    [Header("Components")]
    [Tooltip("Camera sẽ được tập trung vào.")]
    [SerializeField] private CinemachineCamera _focusCamera;

    [Header("Settings")]
    [Tooltip("Thời gian camera giữ ở điểm tập trung (giây).")]
    [SerializeField] private float _focusDuration = 3f;
    
    [Tooltip("Thời gian chờ trước khi bắt đầu chuyển camera (giây).")]
    [SerializeField] private float _startDelay = 0.5f;

    [Tooltip("Ưu tiên của camera khi được kích hoạt (nên cao hơn các camera khác).")]
    [SerializeField] private int _activePriority = 100;

    private Coroutine _focusCoroutine;
    private bool _cutSceneSignalActive;

    private void Awake()
    {
        if (_focusCamera == null)
            _focusCamera = GetComponent<CinemachineCamera>();
            
        SetFocusCameraActive(false);
    }

    private void OnDisable()
    {
        if (_focusCoroutine != null)
        {
            StopCoroutine(_focusCoroutine);
            _focusCoroutine = null;
        }

        SetFocusCameraActive(false);
        EndCutSceneSignal();
    }

    /// <summary>
    /// Bắt đầu chuỗi tập trung camera. Có thể gọi từ UnityEvent.
    /// </summary>
    public void StartFocusSequence()
    {
        if (_focusCamera == null)
        {
            Debug.LogWarning($"[CameraFocusSequence] {gameObject.name} không có Focus Camera!");
            return;
        }

        if (_focusCoroutine != null) return;
        _focusCoroutine = StartCoroutine(FocusRoutine());
    }

    private IEnumerator FocusRoutine()
    {
        yield return new WaitForSeconds(_startDelay);

        // 1. Thông báo bắt đầu Cutscene để khóa điều khiển người chơi
        _cutSceneSignalActive = true;
        EventBus.RaiseCutSceneStarted();

        // 2. Tăng ưu tiên để Cinemachine blend sang camera này
        SetFocusCameraActive(true);

        // 3. Chờ thời gian quan sát
        yield return new WaitForSeconds(_focusDuration);

        // 4. Luôn trả camera focus về trạng thái nghỉ để không tranh quyền
        // với camera người chơi hoặc camera Eris sau khi sequence kết thúc.
        SetFocusCameraActive(false);

        // 5. Thông báo kết thúc Cutscene để trả lại quyền điều khiển
        EndCutSceneSignal();
        _focusCoroutine = null;
    }

    private void SetFocusCameraActive(bool active)
    {
        if (_focusCamera == null) return;
        _focusCamera.Priority.Value = active
            ? Mathf.Max(_activePriority, MinimumFocusPriority)
            : InactivePriority;
    }

    private void EndCutSceneSignal()
    {
        if (!_cutSceneSignalActive) return;
        _cutSceneSignalActive = false;
        EventBus.RaiseCutSceneEnded();
    }

    private void OnValidate()
    {
        _activePriority = Mathf.Max(_activePriority, MinimumFocusPriority);
    }
}
