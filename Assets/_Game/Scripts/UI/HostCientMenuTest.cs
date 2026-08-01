using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HostCientMenuTest : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private GameObject menu;

    private bool _ownsCursor;


    private void Awake()
    {
        hostButton?.onClick.AddListener(OnHostClicked);
        clientButton?.onClick.AddListener(OnClientClicked);
    }

    private void OnEnable()
    {
        SyncCursorOwnership();
        SelectDefaultButton();
    }

    private void Update()
    {
        // This component lives on the persistent test root while the actual menu is a
        // child object. Toggling that child does not invoke OnEnable/OnDisable here.
        SyncCursorOwnership();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

    private void OnDestroy()
    {
        hostButton?.onClick.RemoveListener(OnHostClicked);
        clientButton?.onClick.RemoveListener(OnClientClicked);
        ReleaseCursor();
    }

    private void Start()
    {
        SelectDefaultButton();
    }

    private void OnHostClicked()
    {
        NetworkManager.Singleton.StartHost();
        Hide();
    }

    private void OnClientClicked()
    {
        NetworkManager.Singleton.StartClient();
        Hide();
    }

    private void Hide()
    {
        if (menu != null) menu.SetActive(false);
        ReleaseCursor();
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SyncCursorOwnership()
    {
        bool shouldOwnCursor = menu != null && menu.activeInHierarchy;
        if (shouldOwnCursor == _ownsCursor) return;

        _ownsCursor = shouldOwnCursor;
        if (_ownsCursor)
        {
            UICursorLockService.Request(this);
            CameraManager.Instance?.SetGameplayCameraLocked(true);
        }
        else
        {
            UICursorLockService.Release(this);
            if (!UICursorLockService.IsCursorReleased)
                CameraManager.Instance?.SetGameplayCameraLocked(false);
        }
    }

    private void ReleaseCursor()
    {
        _ownsCursor = false;
        UICursorLockService.Release(this);
        if (!UICursorLockService.IsCursorReleased)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
    }

    private void SelectDefaultButton()
    {
        if (menu != null && !menu.activeInHierarchy) return;
        if (EventSystem.current == null || hostButton == null || !hostButton.interactable) return;

        EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
    }
}
