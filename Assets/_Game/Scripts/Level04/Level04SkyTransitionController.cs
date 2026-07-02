using System.Collections;
using UnityEngine;

public class Level04SkyTransitionController : MonoBehaviour
{
    [SerializeField] private Color _naturalFog = new(0.55f, 0.75f, 0.9f);
    [SerializeField] private Color _galaxyFog = new(0.12f, 0.06f, 0.28f);
    [SerializeField] private Color _returnFog = new(0.65f, 0.72f, 0.75f);
    [SerializeField] private Color _naturalAmbient = new(0.65f, 0.72f, 0.8f);
    [SerializeField] private Color _galaxyAmbient = new(0.2f, 0.12f, 0.38f);
    [SerializeField] private Color _returnAmbient = new(0.8f, 0.65f, 0.48f);
    [SerializeField, Min(0.1f)] private float _transitionDuration = 2.5f;

    private void OnEnable()
    {
        EventBus.OnLevel04PhaseChanged += HandlePhaseChanged;
    }

    private void OnDisable()
    {
        EventBus.OnLevel04PhaseChanged -= HandlePhaseChanged;
    }

    private void HandlePhaseChanged(Level04Phase phase)
    {
        Color fog = phase switch
        {
            Level04Phase.GalaxyGate or Level04Phase.TimeWarpAscent => _galaxyFog,
            Level04Phase.StarfallReturn or Level04Phase.TerrainReveal => _returnFog,
            _ => _naturalFog
        };

        Color ambient = phase switch
        {
            Level04Phase.GalaxyGate or Level04Phase.TimeWarpAscent => _galaxyAmbient,
            Level04Phase.StarfallReturn or Level04Phase.TerrainReveal => _returnAmbient,
            _ => _naturalAmbient
        };

        StopAllCoroutines();
        StartCoroutine(TransitionRoutine(fog, ambient));
    }

    private IEnumerator TransitionRoutine(Color targetFog, Color targetAmbient)
    {
        Color startFog = RenderSettings.fogColor;
        Color startAmbient = RenderSettings.ambientLight;
        float elapsed = 0f;

        while (elapsed < _transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _transitionDuration);
            RenderSettings.fogColor = Color.Lerp(startFog, targetFog, t);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, targetAmbient, t);
            yield return null;
        }
    }
}
