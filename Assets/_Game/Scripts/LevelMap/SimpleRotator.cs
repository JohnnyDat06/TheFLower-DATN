using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed class SimpleRotator : MonoBehaviour
{
    [Tooltip("Tốc độ và hướng xoay của vật thể (X, Y, Z)")]
    [FormerlySerializedAs("rotationSpeed")]
    [SerializeField] private Vector3 _rotationSpeed = new(0f, 0f, 50f);

    [Tooltip("Không gian xoay: Self theo trục local, World theo trục thế giới")]
    [SerializeField] private Space _rotationSpace = Space.Self;

    private Transform _cachedTransform;

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void Update()
    {
        _cachedTransform.Rotate(_rotationSpeed * Time.deltaTime, _rotationSpace);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;

        StaticEditorFlags staticFlags = GameObjectUtility.GetStaticEditorFlags(gameObject);
        if (staticFlags == 0) return;

        // A rotating renderer cannot participate in static batching. If it does,
        // Unity updates the Transform while continuing to draw the baked mesh.
        GameObjectUtility.SetStaticEditorFlags(gameObject, 0);
        PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
        EditorUtility.SetDirty(gameObject);

        if (gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif
}
