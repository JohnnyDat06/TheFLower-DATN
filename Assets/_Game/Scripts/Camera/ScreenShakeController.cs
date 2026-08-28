using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class ScreenShakeController : MonoBehaviour
{
    [SerializeField, Tooltip("Camera Third Person nhận Cinemachine Perlin để xử lý rung màn hình dùng chung.")]
    private CinemachineCamera _vcamThirdPerson;

    private CinemachineBasicMultiChannelPerlin _perlin;
    private float _currentAmplitude;
    private int _activeShakeCount;
    private bool _shakeEnabled = true;
    private float _persistentAmplitude;
    private float _persistentFrequency;

    private void Awake()
    {
        if (_vcamThirdPerson != null)
        {
            _perlin = _vcamThirdPerson.GetComponent<CinemachineBasicMultiChannelPerlin>();
        }

        if (_perlin == null)
        {
            Debug.LogError("[ScreenShakeController] Missing CinemachineBasicMultiChannelPerlin on ThirdPerson VCam.");
        }
    }

    private void OnEnable()
    {
        EventBus.OnScreenShakeRequested += HandleShakeRequested;
        EventBus.OnAccessibilityChanged += LoadAccessibilitySettings;
    }

    private void OnDisable()
    {
        EventBus.OnScreenShakeRequested -= HandleShakeRequested;
        EventBus.OnAccessibilityChanged -= LoadAccessibilitySettings;
    }

    private void Start()
    {
        LoadAccessibilitySettings();
    }

    private void LoadAccessibilitySettings()
    {
        _shakeEnabled = PlayerPrefs.GetInt(Constants.PlayerPrefsKeys.ACCESSIBILITY_CAMERA_SHAKE, 1) == 1;
        if (!_shakeEnabled)
        {
            StopAllShakes();
        }
    }

    private void HandleShakeRequested(SOScreenShakeConfig config)
    {
        if (!_shakeEnabled || config == null)
        {
            return;
        }

        Shake(config);
    }

    public void Shake(SOScreenShakeConfig config)
    {
        if (!_shakeEnabled || config == null || _perlin == null)
        {
            return;
        }

        StartCoroutine(ShakeCoroutine(config));
    }

    private IEnumerator ShakeCoroutine(SOScreenShakeConfig config)
    {
        _activeShakeCount++;
        _currentAmplitude += config.Amplitude;
        ApplyCombinedShake(config.Frequency);

        yield return new WaitForSeconds(config.Duration);

        _currentAmplitude -= config.Amplitude;
        _currentAmplitude = Mathf.Max(0f, _currentAmplitude);
        _activeShakeCount--;

        if (_activeShakeCount <= 0)
        {
            ApplyCombinedShake(_persistentFrequency);
            _activeShakeCount = 0;
        }
        else
        {
            ApplyCombinedShake(config.Frequency);
        }
    }

    /// <summary>Sets a continuous shake layer, used by sustained hazards such as Critical Storm.</summary>
    public void SetPersistentShake(float amplitude, float frequency)
    {
        _persistentAmplitude = _shakeEnabled ? Mathf.Max(0f, amplitude) : 0f;
        _persistentFrequency = Mathf.Max(0f, frequency);
        ApplyCombinedShake(_persistentFrequency);
    }

    public void StopAllShakes()
    {
        StopAllCoroutines();
        _currentAmplitude = 0f;
        _activeShakeCount = 0;
        _persistentAmplitude = 0f;
        _persistentFrequency = 0f;

        if (_perlin != null)
        {
            _perlin.AmplitudeGain = 0f;
            _perlin.FrequencyGain = 0f;
        }
    }

    private void ApplyCombinedShake(float transientFrequency)
    {
        if (_perlin == null)
        {
            return;
        }

        _perlin.AmplitudeGain = _currentAmplitude + _persistentAmplitude;
        _perlin.FrequencyGain = Mathf.Max(transientFrequency, _persistentFrequency);
    }
}
