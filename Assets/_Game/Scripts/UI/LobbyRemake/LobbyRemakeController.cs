using System;
using Game.UI.LobbyAuto;
using UnityEngine;

namespace Game.UI.LobbyRemake
{
    /// <summary>
    /// Compatibility adapter for the first LobbyRemake prototype scene. New scenes should attach
    /// <see cref="LobbyAutoController"/> directly.
    /// </summary>
    [Obsolete("Use LobbyAutoController. This adapter only preserves existing scene references.")]
    public sealed class LobbyRemakeController : MonoBehaviour
    {
        private void Awake()
        {
            if (FindFirstObjectByType<LobbyAutoController>() == null)
                gameObject.AddComponent<LobbyAutoController>();

            enabled = false;
        }
    }
}
