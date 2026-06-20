using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles all "look at and press E" interaction for the local player: picking up
/// loose items, placing a held material on a compatible build tile, hold-to-build
/// when holding the matching tool, and dropping/throwing. Add to the Player prefab
/// alongside PlayerController.
///
/// Requires a HoldPoint child Transform on the camera:
///   Main Camera
///   └── HoldPoint (local position: 0, -0.2, 1.5)
///
/// Targeting is a single forward raycast from playerCamera (Systems Architecture,
/// Section 4.2) -- replaces the old OverlapSphere "nearest in range" pickup.
///
/// Only runs logic for the local owner. Remote players' components are inactive.
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Camera          playerCamera;
    [SerializeField] private PlayerCamera    mouseLook;      // Same Main Camera child as playerCamera, different component
    [SerializeField] private Transform       holdPoint;      // Child of camera, where held object sits
    [SerializeField] private TextMeshProUGUI interactPrompt; // optional, screen-space

    [Header("Settings")]
    [SerializeField] private float interactRange = 2.5f;
    [SerializeField] private float throwForce    = 8f;

    [Header("Interaction Feedback")]
    [SerializeField] private Color validGhostColor   = new(0.2f, 1f,   0.3f, 0.55f);
    [SerializeField] private Color invalidGhostColor = new(1f,   0.2f, 0.2f, 0.30f);
    [SerializeField] private Color hoverOutlineColor = new(1f,   0.9f, 0.2f, 1f);

    private InputReader      _input;
    private PhysicsPickup    _heldObject;
    private OrderStation     _openOrderMenuTarget;
    private LevelSelectKiosk _openKioskMenuTarget;

    // Reused every frame for MaterialPropertyBlock writes -- never allocate one in Update.
    private MaterialPropertyBlock _mpb;
    private BuildTile             _lastGhostTarget;
    private Renderer              _lastOutlineTarget;

    private void Awake()
    {
        _input = GetComponent<InputReader>();
        _mpb = new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (!IsOwner) return;

        MoveHeldObject();

        Physics.Raycast(new Ray(playerCamera.transform.position, playerCamera.transform.forward),
            out RaycastHit hit, interactRange);
        Collider hitCollider = hit.collider;

        BuildTile        tileTarget   = hitCollider != null ? hitCollider.GetComponentInParent<BuildTile>()        : null;
        PhysicsPickup    pickupTarget = hitCollider != null ? hitCollider.GetComponentInParent<PhysicsPickup>()    : null;
        OrderStation     orderTarget  = hitCollider != null ? hitCollider.GetComponentInParent<OrderStation>()     : null;
        LevelSelectKiosk kioskTarget  = hitCollider != null ? hitCollider.GetComponentInParent<LevelSelectKiosk>() : null;
        LevelEditorAccessPoint editorAccessTarget = hitCollider != null ? hitCollider.GetComponentInParent<LevelEditorAccessPoint>() : null;
        EvaluateFeedback(tileTarget, pickupTarget, orderTarget, kioskTarget, editorAccessTarget);

        _debugDemolishTarget = IsServer && tileTarget != null &&
            (tileTarget.State == TileState.MaterialPlaced || tileTarget.State == TileState.Built)
                ? tileTarget : null;

        if (_openOrderMenuTarget != null && _openOrderMenuTarget != orderTarget)
            CloseOrderMenu();
        if (_openKioskMenuTarget != null && _openKioskMenuTarget != kioskTarget)
            CloseKioskMenu();

        UpdatePrompt(tileTarget, orderTarget, kioskTarget, editorAccessTarget);
        HandleContinuousBuild(tileTarget);
        HandleDebugDemolish();
        HandleInteractPress(tileTarget, pickupTarget, orderTarget, kioskTarget, editorAccessTarget);
        HandleOrderMenuSelection();
        HandleKioskMenuSelection();
    }

    // -------------------------------------------------------------------------
    // Debug: host-only manual tile demolish. Stand-in for the chaos event
    // framework (PLANNED_FEATURES.md Phase D), which doesn't exist yet and is
    // the eventual real trigger for BuildTile.Collapse(). Remove this once
    // Termites or another structural chaos event can drive it instead.
    // -------------------------------------------------------------------------

    private BuildTile _debugDemolishTarget;

    private void HandleDebugDemolish()
    {
        if (_debugDemolishTarget == null) return;
        if (!(Keyboard.current?.backspaceKey.wasPressedThisFrame ?? false)) return;

        _debugDemolishTarget.Collapse();
    }

    // -------------------------------------------------------------------------
    // Interaction feedback -- crosshair state, ghost tint on targeted build
    // tiles, and outline highlight on targeted loose pickups. Driven entirely
    // by the raycast targets Update() already computes; no separate raycast.
    // -------------------------------------------------------------------------

    private enum CrosshairState { Default, Hover, PlaceValid, PlaceInvalid, Build }

    private CrosshairState _crosshairState = CrosshairState.Default;

    private void EvaluateFeedback(BuildTile tileTarget, PhysicsPickup pickupTarget,
        OrderStation orderTarget, LevelSelectKiosk kioskTarget, LevelEditorAccessPoint editorAccessTarget)
    {
        CrosshairState newState = CrosshairState.Default;
        BuildTile ghostTarget = null;
        Renderer outlineTarget = null;

        if (tileTarget != null && (tileTarget.State == TileState.Empty || tileTarget.State == TileState.Destroyed))
        {
            // Repairable Destroyed tiles take a fresh material exactly like Empty ones --
            // CanAcceptMaterial already encodes that, so no separate check is needed here.
            var material = _heldObject != null ? _heldObject.GetComponent<MaterialItem>() : null;
            if (material != null)
            {
                // Red is reserved for an actual mismatch -- empty hands (or holding a
                // tool, which doesn't apply here) leaves the ghost at its normal hue
                // instead of flagging every empty tile red by default.
                newState = tileTarget.CanAcceptMaterial(material.Type) ? CrosshairState.PlaceValid : CrosshairState.PlaceInvalid;
                ghostTarget = tileTarget;
            }
            else
            {
                newState = CrosshairState.Hover;
            }
        }
        else if (tileTarget != null && tileTarget.State == TileState.MaterialPlaced)
        {
            var tool = _heldObject != null ? _heldObject.GetComponent<ToolItem>() : null;
            // The hold-to-build progress bar communicates this state once building starts;
            // the crosshair just needs to signal "yes, you can start" beforehand.
            newState = tool != null && tileTarget.CanBuild(tool.Type) ? CrosshairState.Build : CrosshairState.Hover;
        }
        else if (pickupTarget != null && !pickupTarget.IsHeld)
        {
            newState = CrosshairState.Hover;
            outlineTarget = pickupTarget.GetComponentInChildren<Renderer>();
        }
        else if (tileTarget != null || orderTarget != null || kioskTarget != null || editorAccessTarget != null)
        {
            newState = CrosshairState.Hover;
        }

        if (ghostTarget != _lastGhostTarget)
        {
            ClearGhostTint();
            if (ghostTarget != null)
                ApplyGhostTint(ghostTarget, newState == CrosshairState.PlaceValid);
        }

        if (outlineTarget != _lastOutlineTarget)
        {
            ClearOutlineHighlight();
            if (outlineTarget != null)
                ApplyOutlineHighlight(outlineTarget);
        }

        _crosshairState = newState;
    }

    private void ApplyGhostTint(BuildTile tile, bool valid)
    {
        if (tile.GhostRenderer == null) return;

        _mpb.Clear();
        _mpb.SetColor("_BaseColor", valid ? validGhostColor : invalidGhostColor);
        tile.GhostRenderer.SetPropertyBlock(_mpb);
        _lastGhostTarget = tile;
    }

    private void ClearGhostTint()
    {
        if (_lastGhostTarget == null) return;

        if (_lastGhostTarget.GhostRenderer != null)
        {
            _mpb.Clear();
            _lastGhostTarget.GhostRenderer.SetPropertyBlock(_mpb);
        }
        _lastGhostTarget = null;
    }

    private void ApplyOutlineHighlight(Renderer r)
    {
        if (r == null) return;

        _mpb.Clear();
        _mpb.SetColor("_OutlineColor", hoverOutlineColor);
        r.SetPropertyBlock(_mpb);
        _lastOutlineTarget = r;
    }

    private void ClearOutlineHighlight()
    {
        if (_lastOutlineTarget == null) return;

        _mpb.Clear();
        _lastOutlineTarget.SetPropertyBlock(_mpb);
        _lastOutlineTarget = null;
    }

    // -------------------------------------------------------------------------
    // Per-frame
    // -------------------------------------------------------------------------

    private void MoveHeldObject()
    {
        if (_heldObject == null) return;

        // Snap the held object to the hold point each frame.
        // Since we own the object, ClientNetworkTransform syncs this to other clients.
        _heldObject.transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
    }

    // -------------------------------------------------------------------------
    // Continuous hold-to-build (Section 6.2, step 3)
    // -------------------------------------------------------------------------

    private void HandleContinuousBuild(BuildTile target)
    {
        if (target == null || !_input.InteractHeld) return;

        var tool = _heldObject != null ? _heldObject.GetComponent<ToolItem>() : null;
        if (tool == null || !target.CanBuild(tool.Type)) return;

        target.ContinueBuildRpc(OwnerClientId, tool.Type);
    }

    // -------------------------------------------------------------------------
    // Single press: pick up / place / drop
    // -------------------------------------------------------------------------

    private void HandleInteractPress(BuildTile tileTarget, PhysicsPickup pickupTarget, OrderStation orderTarget, LevelSelectKiosk kioskTarget, LevelEditorAccessPoint editorAccessTarget)
    {
        if (!_input.InteractPressed) return;
        _input.ConsumeInteract();

        if (editorAccessTarget != null)
        {
            editorAccessTarget.EnterLevelEditorRpc();
            return;
        }

        if (kioskTarget != null)
        {
            if (_openKioskMenuTarget == kioskTarget)
                CloseKioskMenu(); // pressing E again closes the menu
            else
                OpenKioskMenu(kioskTarget);
            return;
        }

        if (orderTarget != null)
        {
            if (_openOrderMenuTarget == orderTarget)
                CloseOrderMenu(); // pressing E again closes the menu
            else if (!orderTarget.WouldExceedCap)
                OpenOrderMenu(orderTarget);
            return;
        }

        if (_heldObject == null)
        {
            TryPickup(pickupTarget);
            return;
        }

        var material = _heldObject.GetComponent<MaterialItem>();
        if (material != null && tileTarget != null && tileTarget.CanAcceptMaterial(material.Type))
        {
            PlaceHeldMaterial(tileTarget);
            return;
        }

        // Holding a tool over a buildable tile: the press that starts the hold
        // shouldn't also register as "drop" -- building itself is driven by
        // HandleContinuousBuild while E stays held.
        var tool = _heldObject.GetComponent<ToolItem>();
        if (tool != null && tileTarget != null && tileTarget.CanBuild(tool.Type))
            return;

        Drop();
    }

    private void TryPickup(PhysicsPickup pickupTarget)
    {
        if (pickupTarget == null || pickupTarget.IsHeld) return;

        // Request ownership transfer from the host
        pickupTarget.RequestPickupServerRpc(OwnerClientId);
        _heldObject = pickupTarget;
    }

    private void PlaceHeldMaterial(BuildTile target)
    {
        target.PlaceMaterialRpc(_heldObject.NetworkObject);
        _heldObject = null;
    }

    private void Drop()
    {
        if (_heldObject == null) return;

        // Throw in the direction the camera is facing
        Vector3 throwVelocity = playerCamera.transform.forward * throwForce;

        _heldObject.RequestDropServerRpc(throwVelocity);
        _heldObject = null;
    }

    // -------------------------------------------------------------------------
    // Order menu (material picker) -- opened by HandleInteractPress when looking
    // at an OrderStation. Local-only UI state; the actual order is still a
    // server RPC once a material is chosen, same delivery pipeline as before.
    //
    // Selection works two ways: number keys (HandleOrderMenuSelection below) and
    // mouse clicks on UI Buttons wired to SelectOrderOption/CloseOrderMenu -- both
    // funnel through the same methods, so a click and a keypress behave identically.
    // -------------------------------------------------------------------------

    public bool         IsOrderMenuOpen     => _openOrderMenuTarget != null;
    public OrderStation OpenOrderMenuTarget => _openOrderMenuTarget;

    private static readonly Key[] DigitKeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };

    private void HandleOrderMenuSelection()
    {
        if (_openOrderMenuTarget == null || Keyboard.current == null) return;

        int count = Mathf.Min(_openOrderMenuTarget.MaterialCount, DigitKeys.Length);
        for (int i = 0; i < count; i++)
        {
            if (!Keyboard.current[DigitKeys[i]].wasPressedThisFrame) continue;

            SelectOrderOption(i);
            break;
        }
    }

    private void OpenOrderMenu(OrderStation station)
    {
        _openOrderMenuTarget = station;
        if (mouseLook != null) mouseLook.SetLookEnabled(false);
    }

    /// <summary>Wire a UI Button's OnClick to this, with the option's index as its static int argument.</summary>
    public void SelectOrderOption(int index)
    {
        if (_openOrderMenuTarget == null) return;

        _openOrderMenuTarget.PlaceOrderRpc(index);
        CloseOrderMenu();
    }

    /// <summary>Wire a Cancel button's OnClick to this, or call it from anywhere else that should dismiss the menu.</summary>
    public void CloseOrderMenu()
    {
        _openOrderMenuTarget = null;
        if (mouseLook != null) mouseLook.SetLookEnabled(true);
    }

    // -------------------------------------------------------------------------
    // Kiosk menu (blueprint picker) -- Hub-only, opened by HandleInteractPress when
    // looking at a LevelSelectKiosk. Same open/close/number-key-selection shape as the
    // order menu above.
    // -------------------------------------------------------------------------

    public bool             IsKioskMenuOpen     => _openKioskMenuTarget != null;
    public LevelSelectKiosk OpenKioskMenuTarget => _openKioskMenuTarget;

    private void HandleKioskMenuSelection()
    {
        if (_openKioskMenuTarget == null || Keyboard.current == null) return;

        int count = Mathf.Min(_openKioskMenuTarget.OptionCount, DigitKeys.Length);
        for (int i = 0; i < count; i++)
        {
            if (!Keyboard.current[DigitKeys[i]].wasPressedThisFrame) continue;

            SelectKioskOption(i);
            break;
        }
    }

    private void OpenKioskMenu(LevelSelectKiosk kiosk)
    {
        kiosk.RefreshAvailableBlueprints();
        _openKioskMenuTarget = kiosk;
        if (mouseLook != null) mouseLook.SetLookEnabled(false);
    }

    /// <summary>Wire a UI Button's OnClick to this, with the option's index as its static int argument.</summary>
    public void SelectKioskOption(int index)
    {
        if (_openKioskMenuTarget == null) return;

        if (_openKioskMenuTarget.IsLevelEditorOption(index))
            _openKioskMenuTarget.EnterLevelEditorRpc();
        else
            _openKioskMenuTarget.SelectBlueprintRpc(_openKioskMenuTarget.IdAt(index));

        CloseKioskMenu();
    }

    /// <summary>Wire a Cancel button's OnClick to this, or call it from anywhere else that should dismiss the menu.</summary>
    public void CloseKioskMenu()
    {
        _openKioskMenuTarget = null;
        if (mouseLook != null) mouseLook.SetLookEnabled(true);
    }

    // -------------------------------------------------------------------------
    // Prompt
    // -------------------------------------------------------------------------

    private void UpdatePrompt(BuildTile target, OrderStation orderTarget, LevelSelectKiosk kioskTarget, LevelEditorAccessPoint editorAccessTarget)
    {
        if (interactPrompt == null) return;

        if (editorAccessTarget != null)
        {
            interactPrompt.text = "[E] Enter Level Editor";
            return;
        }

        if (kioskTarget != null)
        {
            interactPrompt.text = _openKioskMenuTarget == kioskTarget
                ? "[1-9] Choose Level -- [E] Cancel"
                : "[E] Select Level";
            return;
        }

        if (orderTarget != null)
        {
            interactPrompt.text = _openOrderMenuTarget == orderTarget
                ? "[1-9] Choose Material -- [E] Cancel"
                : orderTarget.WouldExceedCap
                    ? "Material cap reached"
                    : "[E] Order Materials";
            return;
        }

        if (target != null && _heldObject != null)
        {
            var material = _heldObject.GetComponent<MaterialItem>();
            if (material != null && target.CanAcceptMaterial(material.Type))
            {
                interactPrompt.text = "[E] Place Material";
                return;
            }

            var tool = _heldObject.GetComponent<ToolItem>();
            if (tool != null && target.CanBuild(tool.Type))
            {
                interactPrompt.text = "[E] Hold to Build";
                return;
            }
        }

        interactPrompt.text = _heldObject != null ? "[E] Drop" : "";
    }

    // -------------------------------------------------------------------------
    // Crosshair -- a centered dot (plus a ring for context states) so the
    // player can tell exactly what the raycast is aimed at, and whether the
    // current action would succeed, before pressing E. Drawn directly via
    // OnGUI so it needs no Canvas/Image setup in the scene or prefab.
    // -------------------------------------------------------------------------

    private const float CrosshairSize = 6f;
    private const float RingRadius    = 10f;
    private const float RingThickness = 2f;

    private void OnGUI()
    {
        if (!IsOwner) return;

        DrawCrosshair();
        DrawOrderQueue();
        DrawOrderMenu();
        DrawKioskMenu();
        DrawDebugDemolishHint();
    }

    private void DrawCrosshair()
    {
        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);

        switch (_crosshairState)
        {
            case CrosshairState.Hover:
                DrawDot(center, Color.yellow);
                DrawRing(center, Color.yellow);
                break;
            case CrosshairState.PlaceValid:
                DrawDot(center, Color.green);
                DrawRing(center, Color.green);
                break;
            case CrosshairState.PlaceInvalid:
                DrawDot(center, Color.red);
                break;
            case CrosshairState.Build:
                DrawDot(center, Color.white);
                DrawRing(center, Color.white);
                break;
            default:
                DrawDot(center, Color.white);
                break;
        }
    }

    private static void DrawDot(Vector2 center, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(
            center.x - CrosshairSize * 0.5f, center.y - CrosshairSize * 0.5f,
            CrosshairSize, CrosshairSize), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // Hollow square ring (four thin bars) -- reuses the same built-in white texture
    // as DrawDot, no extra texture asset or Awake-time allocation needed.
    private static void DrawRing(Vector2 center, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - RingRadius, center.y - RingRadius, RingRadius * 2f, RingThickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x - RingRadius, center.y + RingRadius - RingThickness, RingRadius * 2f, RingThickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x - RingRadius, center.y - RingRadius, RingThickness, RingRadius * 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x + RingRadius - RingThickness, center.y - RingRadius, RingThickness, RingRadius * 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawDebugDemolishHint()
    {
        if (_debugDemolishTarget == null) return;

        GUI.Label(new Rect(10f, Screen.height - 30f, 460f, 24f),
            "[Backspace] Debug: demolish targeted tile (host only, stand-in for chaos events)");
    }

    // -------------------------------------------------------------------------
    // Order menu rendering -- a small list of selectable materials centered
    // below the crosshair while _openOrderMenuTarget is set.
    // -------------------------------------------------------------------------

    private const float MenuWidth      = 220f;
    private const float MenuLineHeight = 22f;
    private const float MenuPadding    = 8f;

    private void DrawOrderMenu()
    {
        if (_openOrderMenuTarget == null) return;

        int count = _openOrderMenuTarget.MaterialCount;
        float height = MenuPadding * 2f + MenuLineHeight * (count + 1);
        var area = new Rect(
            Screen.width * 0.5f - MenuWidth * 0.5f,
            Screen.height * 0.5f + 20f,
            MenuWidth, height);

        GUI.Box(area, "Order Materials");
        for (int i = 0; i < count; i++)
        {
            var lineRect = new Rect(
                area.x + MenuPadding,
                area.y + MenuPadding + MenuLineHeight * (i + 1),
                MenuWidth - MenuPadding * 2f, MenuLineHeight);
            GUI.Label(lineRect, $"[{i + 1}] {_openOrderMenuTarget.DescribeOption(i)}");
        }
    }

    // -------------------------------------------------------------------------
    // Kiosk menu rendering -- same layout as the order menu, listing blueprints
    // instead of materials while _openKioskMenuTarget is set.
    // -------------------------------------------------------------------------

    private void DrawKioskMenu()
    {
        if (_openKioskMenuTarget == null) return;

        int count = _openKioskMenuTarget.OptionCount;
        float height = MenuPadding * 2f + MenuLineHeight * (count + 1);
        var area = new Rect(
            Screen.width * 0.5f - MenuWidth * 0.5f,
            Screen.height * 0.5f + 20f,
            MenuWidth, height);

        GUI.Box(area, "Select Level");
        for (int i = 0; i < count; i++)
        {
            var lineRect = new Rect(
                area.x + MenuPadding,
                area.y + MenuPadding + MenuLineHeight * (i + 1),
                MenuWidth - MenuPadding * 2f, MenuLineHeight);
            GUI.Label(lineRect, $"[{i + 1}] {_openKioskMenuTarget.DescribeOption(i)}");
        }
    }

    // -------------------------------------------------------------------------
    // Delivery queue -- top-right list of incoming orders (Systems Architecture,
    // Section 5.3). Shared across the team, not per-player, so it reads straight
    // from OrderQueueSystem's replicated list rather than tracking anything locally.
    // -------------------------------------------------------------------------

    private const float QueueWidth      = 220f;
    private const float QueueLineHeight = 20f;
    private const float QueuePadding    = 8f;

    private void DrawOrderQueue()
    {
        if (OrderQueueSystem.Instance == null) return;

        var orders = OrderQueueSystem.Instance.PendingOrders;
        if (orders.Count == 0) return;

        float height = QueuePadding * 2f + QueueLineHeight * (orders.Count + 1);
        var area = new Rect(Screen.width - QueueWidth - 10f, 10f, QueueWidth, height);

        GUI.Box(area, "Incoming Deliveries");
        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var lineRect = new Rect(
                area.x + QueuePadding,
                area.y + QueuePadding + QueueLineHeight * (i + 1),
                QueueWidth - QueuePadding * 2f, QueueLineHeight);
            GUI.Label(lineRect, $"{order.quantity}x {order.materialType} -- {Mathf.CeilToInt(order.remainingSeconds)}s");
        }
    }

    // -------------------------------------------------------------------------
    // Cleanup
    // -------------------------------------------------------------------------

    public override void OnNetworkDespawn()
    {
        // If this player disconnects while holding something, drop it cleanly
        if (_heldObject != null && IsServer)
            _heldObject.RequestDropServerRpc(Vector3.zero);

        ClearGhostTint();
        ClearOutlineHighlight();
    }
}
