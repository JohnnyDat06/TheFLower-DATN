using System.Collections;
using UnityEngine;

public class MusicIntensityController : MonoBehaviour
{
    [SerializeField] private AudioSource _ambientWind;
    [SerializeField] private AudioSource _softPiano;
    [SerializeField] private AudioSource _warmStrings;
    [SerializeField] private AudioSource _emotionalSwell;
    [SerializeField] private AudioSource _calmReturn;
    [SerializeField, Min(0.1f)] private float _fadeDuration = 1.5f;

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
        StopAllCoroutines();
        StartCoroutine(FadeLayers(
            phase,
            phase is Level04Phase.IntroPeak or Level04Phase.WingUnlock ? 0.65f : 0.25f,
            phase == Level04Phase.WingUnlock ? 0.7f : 0f,
            phase is Level04Phase.CloudDescent or Level04Phase.CloudCorridor ? 0.7f : 0f,
            phase is Level04Phase.GalaxyGate or Level04Phase.TimeWarpAscent ? 0.85f : 0f,
            phase is Level04Phase.StarfallReturn or Level04Phase.TerrainReveal ? 0.75f : 0f));
    }

    private IEnumerator FadeLayers(
        Level04Phase phase,
        float ambient,
        float piano,
        float strings,
        float swell,
        float calm)
    {
        AudioSource[] sources = { _ambientWind, _softPiano, _warmStrings, _emotionalSwell, _calmReturn };
        float[] starts = new float[sources.Length];
        float[] targets = { ambient, piano, strings, swell, calm };

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            starts[i] = sources[i].volume;
            if (!sources[i].isPlaying && sources[i].clip != null) sources[i].Play();
        }

        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeDuration);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null) sources[i].volume = Mathf.Lerp(starts[i], targets[i], t);
            }
            yield return null;
        }
    }
}
