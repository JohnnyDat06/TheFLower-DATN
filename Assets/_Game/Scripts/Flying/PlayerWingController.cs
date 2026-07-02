using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum PlayerWingState : byte
{
    Hidden,
    Unlocking,
    Gliding,
    Boosting,
    Recovering,
    Landing,
    Dissolving
}

public class PlayerWingController : NetworkBehaviour
{
    [SerializeField] private GameObject[] _wingRoots;
    [SerializeField] private Animator[] _wingAnimators;
    [SerializeField] private TrailRenderer[] _trails;

    private readonly NetworkVariable<PlayerWingState> _state = new(
        PlayerWingState.Hidden,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public PlayerWingState State => _state.Value;

    private void Awake()
    {
        ResolveReferences();
        ApplyState(PlayerWingState.Hidden);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _state.OnValueChanged += HandleStateChanged;
        ApplyState(_state.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.OnValueChanged -= HandleStateChanged;
        base.OnNetworkDespawn();
    }

    public void SetStateServer(PlayerWingState state)
    {
        if (!IsServer) return;
        _state.Value = state;
    }

    public void PlayBoostLocal(float duration)
    {
        if (!IsOwner) return;
        StopAllCoroutines();
        StartCoroutine(BoostVisualRoutine(duration));
    }

    private IEnumerator BoostVisualRoutine(float duration)
    {
        ApplyState(PlayerWingState.Boosting);
        yield return new WaitForSeconds(duration);
        ApplyState(_state.Value);
    }

    private void HandleStateChanged(PlayerWingState previous, PlayerWingState current)
    {
        ApplyState(current);
    }

    private void ApplyState(PlayerWingState state)
    {
        ResolveReferences();
        if (_wingRoots == null || _wingRoots.Length == 0) return;

        foreach (var wingRoot in _wingRoots)
        {
            if (wingRoot != null) wingRoot.SetActive(state != PlayerWingState.Hidden);
        }

        bool trailEnabled = state is PlayerWingState.Gliding
            or PlayerWingState.Boosting
            or PlayerWingState.Recovering;

        if (_trails != null)
        {
            foreach (var trail in _trails)
            {
                if (trail != null) trail.emitting = trailEnabled;
            }
        }

        if (_wingAnimators != null)
        {
            foreach (var animator in _wingAnimators)
            {
                if (animator != null)
                {
                    animator.speed = state == PlayerWingState.Boosting ? 1.35f : 1f;
                }
            }
        }
    }

    private void ResolveReferences()
    {
        if (_wingRoots == null || _wingRoots.Length == 0)
        {
            var roots = new System.Collections.Generic.List<GameObject>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "wings_unity")
                {
                    roots.Add(child.gameObject);
                }
            }
            _wingRoots = roots.ToArray();
        }

        if (_wingRoots == null || _wingRoots.Length == 0) return;
        if (_wingAnimators == null || _wingAnimators.Length == 0)
        {
            var animators = new System.Collections.Generic.List<Animator>();
            foreach (var wingRoot in _wingRoots)
            {
                if (wingRoot == null) continue;
                var animator = wingRoot.GetComponent<Animator>();
                if (animator != null) animators.Add(animator);
            }
            _wingAnimators = animators.ToArray();
        }

        if (_trails == null || _trails.Length == 0)
        {
            var trails = new System.Collections.Generic.List<TrailRenderer>();
            foreach (var wingRoot in _wingRoots)
            {
                if (wingRoot != null)
                {
                    trails.AddRange(wingRoot.GetComponentsInChildren<TrailRenderer>(true));
                }
            }
            _trails = trails.ToArray();
        }
    }
}
