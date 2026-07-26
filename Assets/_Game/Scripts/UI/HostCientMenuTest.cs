using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HostCientMenuTest : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private GameObject menu;


    private void Awake()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        clientButton.onClick.AddListener(OnClientClicked);
    }

    private void OnEnable()
    {
        UICursorLockService.Request(this);
        CameraManager.Instance?.SetGameplayCameraLocked(true);
        SelectDefaultButton();
    }

    private void OnDisable()
    {
        UICursorLockService.Release(this);
        if (!UICursorLockService.IsCursorReleased)
            CameraManager.Instance?.SetGameplayCameraLocked(false);
    }

    private void OnDestroy()
    {
        UICursorLockService.Release(this);
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
        menu.SetActive(false);
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SelectDefaultButton()
    {
        if (menu != null && !menu.activeInHierarchy) return;
        if (EventSystem.current == null || hostButton == null || !hostButton.interactable) return;

        EventSystem.current.SetSelectedGameObject(hostButton.gameObject);
    }
}
