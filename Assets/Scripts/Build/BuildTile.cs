using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single build tile (Systems Architecture, Section 6). One of these is spawned
/// by BuildSystem per blueprint tile entry, positioned at the tile's grid coordinate
/// (1 Unity unit == 1 grid cell).
///
/// Setup for the prefab:
///   - NetworkObject (registered in the NetworkManager's prefab list)
///   - BoxCollider sized to one grid cell -- this is what PlayerInteraction raycasts against
///   - ghostRenderer: a transparent unlit quad/cube shown while Empty
///   - placedMaterialVisual / builtVisual: child objects toggled on state change
///   - progressBarRoot + progressFill: world-space canvas above the tile (progressFill
///     is a RectTransform scaled 0-1 on X to show build progress)
/// No NetworkTransform needed -- tiles never move after spawning.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BuildTile : NetworkBehaviour
{
    [Header("Visuals -- assign in Inspector")]
    [SerializeField] private MeshRenderer ghostRenderer;
    [SerializeField] private GameObject placedMaterialVisual;
    [SerializeField] private GameObject builtVisual;
    [SerializeField] private Color eligibleColor = new(1f, 1f, 1f, 0.5f);
    [SerializeField] private Color ineligibleColor = new(1f, 1f, 1f, 0.15f);

    [Header("Progress Bar -- world space canvas")]
    [SerializeField] private GameObject progressBarRoot;
    [SerializeField] private RectTransform progressFill;

    private const float BuildPingGrace = 0.3f;
    private const ulong NoBuilder = ulong.MaxValue;

    public Vector3Int GridPosition { get; private set; }
    public TileType Type { get; private set; }
    public MaterialType RequiredMaterial { get; private set; }
    public ToolType RequiredTool { get; private set; }
    public int MaxHealth { get; private set; }
    public TileState State => _state.Value;

    private readonly NetworkVariable<TileState> _state = new(
        TileState.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _buildProgress = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _buildingClientId = new(
        NoBuilder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-only
    private NetworkObject _placedMaterial;
    private float _lastPingTime;
    private Coroutine _buildCoroutine;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        GridPosition = Vector3Int.RoundToInt(transform.position);

        TileData data = BuildSystem.Instance.GetTileDataAt(GridPosition);
        if (data == null)
        {
            Debug.LogError($"[BuildTile] No blueprint tile data at {GridPosition}");
            return;
        }

        Type             = BlueprintEnums.ParseTileType(data.type);
        RequiredMaterial = data.requiredMaterial == "any" ? MaterialType.Any : BlueprintEnums.ParseMaterialType(data.requiredMaterial);
        RequiredTool     = data.requiredTool == "none" ? ToolType.None : BlueprintEnums.ParseToolType(data.requiredTool);
        MaxHealth        = data.health;

        BuildSystem.Instance.RegisterTile(GridPosition, this);

        _state.OnValueChanged += OnStateChanged;
        _buildProgress.OnValueChanged += (_, val) => UpdateProgressBar(val);

        RefreshVisual();
    }

    public override void OnNetworkDespawn()
    {
        BuildSystem.Instance.UnregisterTile(GridPosition);
        _state.OnValueChanged -= OnStateChanged;
    }

    private void OnStateChanged(TileState previous, TileState now)
    {
        RefreshVisual();
        if (now == TileState.Built)
            BuildSystem.Instance.RefreshNeighborsOf(GridPosition);
    }

    public void RefreshEligibility() => RefreshVisual();

    // -------------------------------------------------------------------------
    // Visual (Section 6.4) -- pure function of replicated state, runs on every client
    // -------------------------------------------------------------------------

    private void RefreshVisual()
    {
        bool eligible = BuildSystem.Instance.IsEligible(this);

        if (ghostRenderer != null)
        {
            ghostRenderer.enabled = _state.Value == TileState.Empty;
            ghostRenderer.material.color = eligible ? eligibleColor : ineligibleColor;
        }

        if (placedMaterialVisual != null)
            placedMaterialVisual.SetActive(_state.Value == TileState.MaterialPlaced);

        if (builtVisual != null)
            builtVisual.SetActive(_state.Value == TileState.Built);

        if (progressBarRoot != null)
            progressBarRoot.SetActive(_state.Value == TileState.MaterialPlaced && _buildProgress.Value > 0f);
    }

    private void UpdateProgressBar(float elapsed)
    {
        if (progressFill != null)
        {
            float duration = ToolStats.BuildDuration(RequiredTool);
            float pct = duration <= 0f ? 0f : Mathf.Clamp01(elapsed / duration);
            progressFill.localScale = new Vector3(pct, 1f, 1f);
        }

        if (progressBarRoot != null)
            progressBarRoot.SetActive(_state.Value == TileState.MaterialPlaced && elapsed > 0f);
    }

    // -------------------------------------------------------------------------
    // Place material (Section 6.2, step 2)
    // -------------------------------------------------------------------------

    public bool CanAcceptMaterial(MaterialType material) =>
        _state.Value == TileState.Empty
        && BuildSystem.Instance.IsEligible(this)
        && (RequiredMaterial == MaterialType.Any || RequiredMaterial == material);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PlaceMaterialRpc(NetworkObjectReference materialRef)
    {
        if (!materialRef.TryGet(out NetworkObject materialObj)) return;

        var material = materialObj.GetComponent<MaterialItem>();
        if (material == null) return;
        if (!CanAcceptMaterial(material.Type)) return;

        material.PlaceOnTile(transform.position);
        _placedMaterial = materialObj;
        _state.Value = TileState.MaterialPlaced;
    }

    // -------------------------------------------------------------------------
    // Hold-to-build (Section 6.2, step 3). The builder does not have to be the
    // same player who placed the material -- that split is intentional.
    // -------------------------------------------------------------------------

    public bool CanBuild(ToolType heldTool) =>
        _state.Value == TileState.MaterialPlaced && heldTool == RequiredTool;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ContinueBuildRpc(ulong clientId, ToolType heldTool)
    {
        if (!CanBuild(heldTool)) return;

        if (_buildingClientId.Value != clientId)
        {
            _buildingClientId.Value = clientId;
            _buildProgress.Value = 0f;
        }

        _lastPingTime = Time.time;

        if (_buildCoroutine == null)
            _buildCoroutine = StartCoroutine(BuildTickCoroutine());
    }

    private IEnumerator BuildTickCoroutine()
    {
        float duration = ToolStats.BuildDuration(RequiredTool);

        while (_state.Value == TileState.MaterialPlaced)
        {
            if (Time.time - _lastPingTime > BuildPingGrace)
            {
                ResetBuildProgress();
                yield break;
            }

            _buildProgress.Value += Time.deltaTime;

            if (_buildProgress.Value >= duration)
            {
                CompleteBuild();
                yield break;
            }

            yield return null;
        }

        _buildCoroutine = null;
    }

    private void ResetBuildProgress()
    {
        _buildProgress.Value = 0f;
        _buildingClientId.Value = NoBuilder;
        _buildCoroutine = null;
    }

    private void CompleteBuild()
    {
        if (_placedMaterial != null)
        {
            _placedMaterial.GetComponent<MaterialItem>()?.ConsumeAsBuilt();
            _placedMaterial = null;
        }

        _buildProgress.Value = 0f;
        _buildingClientId.Value = NoBuilder;
        _buildCoroutine = null;
        _state.Value = TileState.Built;
    }
}
