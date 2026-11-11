using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkTransform.
/// 
/// The default NetworkTransform is server-authoritative: only the server can change
/// the transform, and any client-side changes are overwritten on the next sync tick.
/// This causes client-owned karts to "not move" even though input is being processed.
/// 
/// This subclass flips authority to the OWNER, so the owning client's transform
/// changes (from PlayerMovement) are accepted and replicated to all other clients.
/// 
/// SETUP:
/// 1. On the player prefab, REMOVE the existing NetworkTransform component.
/// 2. Add this ClientNetworkTransform component instead.
/// 3. No other changes needed — PlayerMovement's existing IsOwner checks handle the rest.
/// 
/// This approach is client-authoritative and fully compatible with Unity Relay.
/// </summary>
public class ClientNetworkTransform : NetworkTransform
{
    /// <summary>
    /// Returning false makes this transform owner-authoritative instead of server-authoritative.
    /// The owning client can modify the transform, and changes are synced to all other clients.
    /// </summary>
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
