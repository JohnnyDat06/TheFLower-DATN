using UnityEngine;

/// <summary>
/// Bridges a route step to an existing interactable without adding quest behaviour to that object.
/// Place this component on a dedicated quest setup object and reference the gameplay interactable.
/// </summary>
public sealed class QuestInteractionLink : MonoBehaviour
{
    [SerializeField] private InteractableBase interactable;
    [SerializeField] private string interactionId;
    [Tooltip("Object that receives the quest outline. Defaults to the referenced interactable, then this link object.")]
    [SerializeField] private Transform markerTarget;

    public string InteractionId => interactable != null ? interactable.InteractableId : interactionId;
    public Transform MarkerTarget => markerTarget != null ? markerTarget : interactable != null ? interactable.transform : transform;

    private void OnValidate()
    {
        if (interactable != null)
            interactionId = interactable.InteractableId;
    }
}
