using Game.UI.LobbyAuto;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Networking.LobbySystem
{
    /// <summary>Swaps the selected Chibi Monkey prefab while preserving player/network animation control.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyCharacterAppearance : MonoBehaviour
    {
        private const string RuntimeConfigResourcePath = "UI/LobbyRuntimeConfig";

        [SerializeField] private Transform _maleModel;
        [SerializeField] private Transform _femaleModel;
        [SerializeField] private SkinnedMeshRenderer _characterRenderer;

        private LobbyRuntimeConfig _config;
        private Animator _playerAnimator;
        private Animator _modelAnimator;
        private GameObject _spawnedModel;
        private int _appliedIndex = -1;

        private void Awake()
        {
            _playerAnimator = GetComponent<Animator>();
            ResolveReferences();
        }

        /// <summary>Applies a zero-based character choice where 0 maps to Chibi_Monkey_00.</summary>
        public bool ApplyCharacter(int index)
        {
            if (index < 0 || index >= LobbyPlayerState.AvailableCharacterCount) return false;

            ResolveReferences();
            GameObject[] characterModels = _config != null ? _config.CharacterModels : null;
            GameObject selectedModel = characterModels != null && index < characterModels.Length
                ? characterModels[index]
                : null;

            if (selectedModel == null) return ApplyMaterialFallback(index);
            if (_spawnedModel != null && _appliedIndex == index) return true;

            DestroySpawnedModel();
            DisableLegacyRenderers();

            _spawnedModel = Instantiate(selectedModel, transform, false);
            _spawnedModel.name = $"Chibi_Monkey_{index:00}";
            _spawnedModel.transform.localPosition = Vector3.zero;
            _spawnedModel.transform.localRotation = Quaternion.identity;
            float modelScale = _maleModel != null ? _maleModel.localScale.x : 0.8f;
            _spawnedModel.transform.localScale = Vector3.one * modelScale;
            _modelAnimator = _spawnedModel.GetComponentInChildren<Animator>(true);
            _appliedIndex = index;
            SyncModelAnimatorController();
            SetSpawnedRenderersVisible(!IsLobbyScene());
            return true;
        }

        private void LateUpdate()
        {
            if (_spawnedModel == null) return;

            DisableLegacyRenderers();
            SetSpawnedRenderersVisible(!IsLobbyScene());
            SyncModelAnimatorController();
            SyncAnimatorParameters();
        }

        private void ResolveReferences()
        {
            _config ??= Resources.Load<LobbyRuntimeConfig>(RuntimeConfigResourcePath);
            _maleModel ??= FindDeepChild(transform, "MeshMale");
            _femaleModel ??= FindDeepChild(transform, "MeshFemale");
            _characterRenderer ??= _maleModel != null
                ? _maleModel.GetComponentInChildren<SkinnedMeshRenderer>(true)
                : GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        private bool ApplyMaterialFallback(int index)
        {
            Material[] materials = _config != null ? _config.CharacterMaterials : null;
            if (_characterRenderer == null || materials == null || index >= materials.Length || materials[index] == null)
                return false;

            if (_maleModel != null) _maleModel.gameObject.SetActive(true);
            if (_femaleModel != null) _femaleModel.gameObject.SetActive(false);
            _characterRenderer.sharedMaterial = materials[index];
            _appliedIndex = index;
            return true;
        }

        private void DestroySpawnedModel()
        {
            if (_spawnedModel == null) return;
            Destroy(_spawnedModel);
            _spawnedModel = null;
            _modelAnimator = null;
            _appliedIndex = -1;
        }

        private void DisableLegacyRenderers()
        {
            SetRenderersVisible(_maleModel, false);
            SetRenderersVisible(_femaleModel, false);
        }

        private void SetSpawnedRenderersVisible(bool visible)
        {
            SetRenderersVisible(_spawnedModel != null ? _spawnedModel.transform : null, visible);
        }

        private static void SetRenderersVisible(Transform root, bool visible)
        {
            if (root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;
        }

        private void SyncModelAnimatorController()
        {
            if (_modelAnimator == null || _playerAnimator == null || _playerAnimator.runtimeAnimatorController == null)
                return;
            if (_modelAnimator.runtimeAnimatorController == _playerAnimator.runtimeAnimatorController) return;

            _modelAnimator.runtimeAnimatorController = _playerAnimator.runtimeAnimatorController;
            _modelAnimator.applyRootMotion = false;
            _modelAnimator.Rebind();
            _modelAnimator.Update(0f);
        }

        private void SyncAnimatorParameters()
        {
            if (_playerAnimator == null || _modelAnimator == null || !_modelAnimator.enabled) return;

            foreach (AnimatorControllerParameter source in _playerAnimator.parameters)
            {
                if (!TryFindParameter(_modelAnimator, source.nameHash, out AnimatorControllerParameter target)) continue;
                switch (source.type)
                {
                    case AnimatorControllerParameterType.Float:
                        _modelAnimator.SetFloat(target.nameHash, _playerAnimator.GetFloat(source.nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        _modelAnimator.SetInteger(target.nameHash, _playerAnimator.GetInteger(source.nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        _modelAnimator.SetBool(target.nameHash, _playerAnimator.GetBool(source.nameHash));
                        break;
                }
            }

            SyncAnimatorStates();
        }

        private void SyncAnimatorStates()
        {
            int layerCount = Mathf.Min(_playerAnimator.layerCount, _modelAnimator.layerCount);
            for (int layer = 0; layer < layerCount; layer++)
            {
                AnimatorStateInfo sourceState = _playerAnimator.GetCurrentAnimatorStateInfo(layer);
                AnimatorStateInfo modelState = _modelAnimator.GetCurrentAnimatorStateInfo(layer);
                if (sourceState.fullPathHash != modelState.fullPathHash)
                    _modelAnimator.Play(sourceState.fullPathHash, layer, sourceState.normalizedTime);
            }
        }

        /// <summary>Mirrors one-shot animation triggers that cannot be read back from the source Animator.</summary>
        public void MirrorTrigger(int parameterHash)
        {
            SyncModelAnimatorController();
            if (_modelAnimator == null || !TryFindParameter(_modelAnimator, parameterHash, out AnimatorControllerParameter parameter)) return;
            if (parameter.type == AnimatorControllerParameterType.Trigger) _modelAnimator.SetTrigger(parameter.nameHash);
        }

        private static bool TryFindParameter(Animator animator, int hash, out AnimatorControllerParameter parameter)
        {
            foreach (AnimatorControllerParameter candidate in animator.parameters)
            {
                if (candidate.nameHash == hash)
                {
                    parameter = candidate;
                    return true;
                }
            }

            parameter = null;
            return false;
        }

        private static bool IsLobbyScene()
        {
            return SceneManager.GetActiveScene().name.Contains("Lobby", System.StringComparison.OrdinalIgnoreCase);
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                Transform result = FindDeepChild(child, childName);
                if (result != null) return result;
            }

            return null;
        }
    }
}
