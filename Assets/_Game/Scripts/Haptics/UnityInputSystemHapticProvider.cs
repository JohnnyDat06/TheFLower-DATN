using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// UnityInputSystemHapticProvider — Implement IHapticProvider bằng Gamepad API.
/// Dùng Gamepad.current.SetMotorSpeeds() và tự dừng qua Coroutine sau duration giây.
/// Fallback graceful nếu không có gamepad kết nối.
/// </summary>
public class UnityInputSystemHapticProvider : MonoBehaviour, IHapticProvider
{
    private Coroutine _stopCoroutine;

    /// <inheritdoc />
    public bool IsSupported => Gamepad.current != null;

    /// <inheritdoc />
    public void Play(float lowMotor, float highMotor, float duration)
    {
        Gamepad gamepad = Gamepad.current;
        if (gamepad == null) return;

        // Dừng coroutine cũ nếu đang chạy (override rung cũ)
        if (_stopCoroutine != null)
        {
            StopCoroutine(_stopCoroutine);
            _stopCoroutine = null;
        }

        gamepad.SetMotorSpeeds(lowMotor, highMotor);
        _stopCoroutine = StartCoroutine(StopAfterDelay(duration));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_stopCoroutine != null)
        {
            StopCoroutine(_stopCoroutine);
            _stopCoroutine = null;
        }

        Gamepad.current?.SetMotorSpeeds(0f, 0f);
    }

    private IEnumerator StopAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Gamepad.current?.SetMotorSpeeds(0f, 0f);
        _stopCoroutine = null;
    }

    private void OnDisable()
    {
        // Đảm bảo motor luôn được tắt khi object bị disable
        Stop();
    }

    private void OnDestroy()
    {
        Stop();
    }
}