using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to an always-active object (e.g. Player.prefab's "Canvas"), not to the panel
/// itself -- a disabled GameObject's Update() never runs, so something else has to be
/// the one flipping it on. Same shape as OrderMenuPanel/KioskMenuPanel, extended for
/// HubTerminal's richer per-row detail line, top-down preview texture, and
/// highlight/confirm-flash state.
/// </summary>
public class TerminalMenuPanel : MonoBehaviour
{
    [SerializeField] private PlayerInteraction playerInteraction;
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject[] rows;
    [SerializeField] private TextMeshProUGUI[] rowLabels;
    [SerializeField] private TextMeshProUGUI[] rowDetails;
    [SerializeField] private RawImage previewImage;
    [SerializeField] private GameObject confirmedLabel;

    private static readonly Color HighlightColor = Color.yellow;
    private static readonly Color NormalColor    = Color.white;

    private void Update()
    {
        bool open = playerInteraction.IsTerminalMenuOpen;
        panel.SetActive(open);
        if (!open) return;

        HubTerminal terminal = playerInteraction.OpenTerminalMenuTarget;
        int highlighted = playerInteraction.TerminalHighlightedIndex;
        int count = Mathf.Min(terminal.OptionCount, rows.Length);

        for (int i = 0; i < rows.Length; i++)
        {
            bool active = i < count;
            rows[i].SetActive(active);
            if (!active) continue;

            rowLabels[i].text = $"[{i + 1}] {terminal.DescribeOption(i)}";
            rowLabels[i].color = i == highlighted ? HighlightColor : NormalColor;
            rowDetails[i].text = terminal.DescribeDetails(i);
        }

        previewImage.texture = terminal.GetPreviewTexture(highlighted);
        confirmedLabel.SetActive(playerInteraction.TerminalFlashActive);
    }
}
