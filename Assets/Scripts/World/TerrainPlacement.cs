using UnityEngine;

/// <summary>
/// Posiziona oggetti sul terreno reale (la mesh con collider) con un raycast verso
/// il centro del pianeta. È il modo standard e robusto: l'oggetto poggia esattamente
/// sulla superficie che vedi, senza il disallineamento tra noise teorico e triangoli.
/// Vale per qualsiasi oggetto, a qualsiasi quota.
/// </summary>
public static class TerrainPlacement
{
    public static bool TryPlaceOnSurface(Vector3 planetCenter, Vector3 dir, float startRadius,
                                         out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        dir = dir.normalized;
        Vector3 from = planetCenter + dir * startRadius;   // ben sopra la vetta più alta
        if (Physics.Raycast(from, -dir, out RaycastHit hit, startRadius))
        {
            surfacePoint = hit.point;
            surfaceNormal = hit.normal;
            return true;
        }
        surfacePoint = from;
        surfaceNormal = dir;
        return false;
    }
}
