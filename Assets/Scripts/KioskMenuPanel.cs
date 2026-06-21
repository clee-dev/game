using TMPro;
using UnityEngine;

/// <summary>
/// Attach to an always-active object (e.g. Player.prefab's "Canvas"), not to the panel
/// itself -- a disabled GameObject's Update() never runs, so something else has to be
/// the one flipping it on. Same shape as OrderMenuPanel, extended for a variable-length
/// (but capped at 9, matching PlayerInteraction's number-key selection cap) option list.
/// </summary>
public class KioskMenuPanel : MonoBehaviour
{
    [SerializeField] private PlayerInteraction playerInteraction;
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject[] rows;
    [SerializeField] private TextMeshProUGUI[] rowLabels;

    private void Update()
    {
        bool open = playerInteraction.IsKioskMenuOpen;
        panel.SetActive(open);
        if (!open) return;

        LevelSelectKiosk kiosk = playerInteraction.OpenKioskMenuTarget;
        int count = Mathf.Min(kiosk.OptionCount, rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            bool active = i < count;
            rows[i].SetActive(active);
            if (active) rowLabels[i].text = $"[{i + 1}] {kiosk.DescribeOption(i)}";
        }
    }
}
