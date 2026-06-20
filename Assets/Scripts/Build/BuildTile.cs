using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single build tile (Systems Architecture, Section 6). One of these is spawned
/// by BuildSystem per blueprint tile entry, positioned at the tile's grid coordinate
/// scaled by BuildSystem.CellSize world units per cell.
///
/// Setup for the prefab:
///   - NetworkObject (registered in the NetworkManager's prefab list)
///   - BoxCollider noticeably smaller than CellSize (e.g. 1.4 units, for a 2-unit
///     cell) so adjacent tiles leave a gap -- this is what PlayerInteraction
///     raycasts against, and flush colliders make it hard to aim at one tile
///     over its neighbor
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

    [Header("Per-material hue -- ghost shows the raw hue, Placed/Built lerp toward blue/green")]
    [SerializeField] [Range(0f, 1f)] private float placedBlueBlend = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float builtGreenBlend = 0.5f;
    [SerializeField] [Range(0f, 1f)] private float destroyedRedBlend = 0.6f;

    // Cached MeshRenderers on placedMaterialVisual/builtVisual -- both GameObjects carry a
    // MeshRenderer directly on their own root, so no extra Inspector fields are needed.
    private MeshRenderer _placedRenderer;
    private MeshRenderer _builtRenderer;

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
        GridPosition = Vector3Int.RoundToInt(transform.position / BuildSystem.CellSize);

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

        if (placedMaterialVisual != null) _placedRenderer = placedMaterialVisual.GetComponent<MeshRenderer>();
        if (builtVisual != null) _builtRenderer = builtVisual.GetComponent<MeshRenderer>();

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
        {
            BuildSystem.Instance.RefreshNeighborsOf(GridPosition);
            BuildSystem.Instance.EvaluateCompletion();
        }
        else if (now == TileState.Destroyed && IsServer)
        {
            // Server-only: re-derives support for whatever this tile was holding up and
            // collapses it too if nothing else is supporting it. Runs once per machine via
            // the IsServer guard -- _state replicates everywhere, but only the server may
            // write the NetworkVariables a further collapse would touch.
            BuildSystem.Instance.CascadeCollapseFrom(GridPosition);
        }
    }

    public void RefreshEligibility() => RefreshVisual();

    // -------------------------------------------------------------------------
    // Progress bar billboard -- only runs while the bar is actually showing (i.e.
    // someone is actively building this tile), so idle tiles cost nothing here.
    // Camera.main is always the local player's camera; NetworkPlayer disables every
    // other player's camera component, so this needs no IsOwner/ownership check.
    // -------------------------------------------------------------------------

    private void LateUpdate()
    {
        if (progressBarRoot == null || !progressBarRoot.activeSelf) return;
        if (Camera.main == null) return;

        Vector3 toCamera = Camera.main.transform.position - progressBarRoot.transform.position;
        toCamera.y = 0f; // yaw only, keep the bar upright instead of tilting to camera height
        if (toCamera.sqrMagnitude > 0.0001f)
            progressBarRoot.transform.rotation = Quaternion.LookRotation(toCamera);
    }

    // -------------------------------------------------------------------------
    // Visual (Section 6.4) -- pure function of replicated state, runs on every client
    // -------------------------------------------------------------------------

    private void RefreshVisual()
    {
        bool eligible = BuildSystem.Instance.IsEligible(this);
        Color hue = BaseHueFor(RequiredMaterial);

        if (ghostRenderer != null)
        {
            bool isDestroyed = _state.Value == TileState.Destroyed;
            ghostRenderer.enabled = _state.Value == TileState.Empty || isDestroyed;
            float alpha = eligible ? eligibleColor.a : ineligibleColor.a;
            Color tint = isDestroyed ? Color.Lerp(hue, Color.red, destroyedRedBlend) : hue;
            ghostRenderer.material.color = new Color(tint.r, tint.g, tint.b, alpha);
        }

        if (placedMaterialVisual != null)
            placedMaterialVisual.SetActive(_state.Value == TileState.MaterialPlaced);
        if (_placedRenderer != null)
            _placedRenderer.material.color = Color.Lerp(hue, Color.blue, placedBlueBlend);

        if (builtVisual != null)
            builtVisual.SetActive(_state.Value == TileState.Built);
        if (_builtRenderer != null)
            _builtRenderer.material.color = Color.Lerp(hue, Color.green, builtGreenBlend);

        if (progressBarRoot != null)
            progressBarRoot.SetActive(_state.Value == TileState.MaterialPlaced && _buildProgress.Value > 0f);
    }

    /// <summary>Stand-in for real per-material textures (none exist yet -- see
    /// docs/SESSION.md). Placed/Built tint this hue toward blue/green; Ghost shows it raw
    /// at the eligible/ineligible alpha.</summary>
    private static Color BaseHueFor(MaterialType material) => material switch
    {
        MaterialType.Wood     => new Color(0.76f, 0.60f, 0.42f),
        MaterialType.Concrete => new Color(0.65f, 0.65f, 0.65f),
        MaterialType.Steel    => new Color(0.55f, 0.58f, 0.62f),
        _                     => new Color(0.8f, 0.8f, 0.8f),
    };

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

    // Destroyed is repairable -- it accepts a fresh material placement exactly like
    // Empty, provided whatever was supporting this tile is still standing.
    public bool CanAcceptMaterial(MaterialType material) =>
        (_state.Value == TileState.Empty || _state.Value == TileState.Destroyed)
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

    // -------------------------------------------------------------------------
    // Structural collapse (Section 4.4 / 6.3) -- server-only, called by
    // BuildSystem.CascadeCollapseFrom (dependents that lost support) and by the
    // debug demolish trigger in PlayerInteraction (stand-in for chaos events,
    // which are the eventual real trigger -- see PLANNED_FEATURES.md Phase D).
    // -------------------------------------------------------------------------

    public void Collapse()
    {
        if (_state.Value == TileState.Destroyed) return;

        if (_buildCoroutine != null)
        {
            StopCoroutine(_buildCoroutine);
            _buildCoroutine = null;
        }

        // A Built tile's material was already despawned by ConsumeAsBuilt; a
        // MaterialPlaced tile still has its raw material sitting on it -- that goes
        // down with the tile instead of being left floating with nothing under it.
        if (_placedMaterial != null)
        {
            _placedMaterial.GetComponent<MaterialItem>()?.DestroyInCollapse();
            _placedMaterial = null;
        }

        _buildProgress.Value = 0f;
        _buildingClientId.Value = NoBuilder;
        _state.Value = TileState.Destroyed;
    }
}
