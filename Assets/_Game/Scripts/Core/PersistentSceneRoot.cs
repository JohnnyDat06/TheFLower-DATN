using UnityEngine;

public static class PersistentSceneRoot
{
    public static void MarkDontDestroyOnLoad(Transform source)
    {
        if (source == null) return;

        Transform target = source;
        if (target.parent != null)
            target.SetParent(null, true);

        Object.DontDestroyOnLoad(target.gameObject);
    }
}
