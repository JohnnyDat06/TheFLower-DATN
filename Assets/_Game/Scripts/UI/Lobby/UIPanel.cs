using System.Collections;
using UnityEngine;

namespace Game.UI.Lobby
{
    [RequireComponent(typeof(CanvasGroup))]
    public class UIPanel : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.25f;
        
        private CanvasGroup _canvasGroup;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(1f));
        }

        public virtual void Hide()
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(0f, () => gameObject.SetActive(false)));
        }
        
        public void HideInstant()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        private IEnumerator FadeRoutine(float targetAlpha, System.Action onComplete = null)
        {
            float startAlpha = _canvasGroup.alpha;
            float time = 0f;

            _canvasGroup.blocksRaycasts = targetAlpha > 0.5f;
            _canvasGroup.interactable = targetAlpha > 0.5f;

            while (time < fadeDuration)
            {
                time += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            onComplete?.Invoke();
        }
    }
}
