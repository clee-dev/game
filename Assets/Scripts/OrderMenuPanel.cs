using UnityEngine;

/// <summary>
/// Attach to an always-active object (e.g. Player.prefab's "Canvas"), not to the panel
/// itself -- a disabled GameObject's Update() never runs, so something else has to be
/// the one flipping it on.
/// </summary>
public class OrderMenuPanel : MonoBehaviour
{
    [SerializeField] private PlayerInteraction playerInteraction;
    [SerializeField] private GameObject panel;

    private void Update()
    {
        panel.SetActive(playerInteraction.IsOrderMenuOpen);
    }
}
