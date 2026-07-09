using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    [Header("Hover - Wiggle & Scale")]
    public float hoverScale = 1.15f;
    public float wiggleIntensity = 3f;
    public float wiggleSpeed = 20f;

    [Header("Click - Squash")]
    public float clickScale = 0.85f;

    [Header("Settings")]
    public float animationSpeed = 12f;

    [Header("Audio")]
    public SOAudioClip hoverSFX;
    public SOAudioClip clickSFX;

    private Vector3 _originalScale;
    private Quaternion _originalRotation;
    private Vector3 _targetScale;
    private bool _isHovering;
    private Coroutine _juiceCoroutine;

    private void Awake()
    {
        _originalScale = transform.localScale;
        _originalRotation = transform.localRotation;
        _targetScale = _originalScale;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        transform.localScale = _originalScale;
        transform.localRotation = _originalRotation;
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHovering(true);

    public void OnPointerExit(PointerEventData eventData) => SetHovering(false);

    public void OnPointerDown(PointerEventData eventData) => Press();

    public void OnPointerUp(PointerEventData eventData) => Release();

    public void OnSelect(BaseEventData eventData) => SetHovering(true);

    public void OnDeselect(BaseEventData eventData) => SetHovering(false);

    public void OnSubmit(BaseEventData eventData)
    {
        Press();
        Release();
    }

    private void SetHovering(bool hovering)
    {
        _isHovering = hovering;
        _targetScale = hovering ? _originalScale * hoverScale : _originalScale;

        if (hovering)
        {
            transform.localScale = _originalScale * (hoverScale + 0.1f);

            if (hoverSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlayUISFX(hoverSFX);
        }
        else if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopUISFX();
        }

        StartJuice();
    }

    private void Press()
    {
        _targetScale = _originalScale * clickScale;
        StartJuice();

        if (clickSFX != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayUISFX(clickSFX);
    }

    private void Release()
    {
        _targetScale = _isHovering ? _originalScale * hoverScale : _originalScale;
        StartJuice();
    }

    private void StartJuice()
    {
        if (!gameObject.activeInHierarchy) return;
        if (_juiceCoroutine != null) StopCoroutine(_juiceCoroutine);
        _juiceCoroutine = StartCoroutine(JuiceRoutine());
    }

    private IEnumerator JuiceRoutine()
    {
        while (true)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.unscaledDeltaTime * animationSpeed);

            if (_isHovering)
            {
                float angle = Mathf.Sin(Time.unscaledTime * wiggleSpeed) * wiggleIntensity;
                transform.localRotation = Quaternion.Euler(0, 0, angle);
            }
            else
            {
                transform.localRotation = Quaternion.Lerp(transform.localRotation, _originalRotation, Time.unscaledDeltaTime * animationSpeed);
            }

            if (!_isHovering &&
                Vector3.Distance(transform.localScale, _originalScale) < 0.001f &&
                Quaternion.Angle(transform.localRotation, _originalRotation) < 0.1f)
            {
                transform.localScale = _originalScale;
                transform.localRotation = _originalRotation;
                yield break;
            }

            yield return null;
        }
    }
}
