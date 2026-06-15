using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Overrides NGO's default NetworkTransform to sync from the owner (client)
/// instead of the server. This means each player's machine is authoritative
/// over their own position -- which is what we want for a first-person game.
///
/// Replace the default NetworkTransform on your player prefab with this.
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
