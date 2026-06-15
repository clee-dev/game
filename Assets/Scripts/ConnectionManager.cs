using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Simple host/join UI for prototype testing.
/// Host binds to localhost, client connects to localhost.
/// This gets replaced by the Steam lobby system in Phase 4.
/// </summary>
public class ConnectionManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private Button     hostButton;
    [SerializeField] private Button     clientButton;

    private void Start()
    {
        hostButton.onClick.AddListener(StartHost);
        clientButton.onClick.AddListener(StartClient);

        NetworkManager.Singleton.OnClientConnectedCallback    += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback   += OnClientDisconnected;
    }

    private void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        connectionPanel.SetActive(false);
        Debug.Log("Started as Host");
    }

    private void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        connectionPanel.SetActive(false);
        Debug.Log("Started as Client, connecting...");
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected. Total players: {NetworkManager.Singleton.ConnectedClients.Count}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected.");

        // If we lose connection as a client, show the panel again
        if (!NetworkManager.Singleton.IsHost)
            connectionPanel.SetActive(true);
    }

    private void OnDestroy()
    {
        // Always unsubscribe from NGO events -- they persist across scene loads
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }
}
