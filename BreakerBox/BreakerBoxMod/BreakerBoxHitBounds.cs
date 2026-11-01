using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxHitBounds
{
    public static Bounds Create(Bounds meshBounds, bool lever, float shellPlaneZ)
    {
        Bounds bounds = meshBounds;
        if (lever)
        {
            for (int angle = 0; angle <= 75; angle += 5)
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    bounds.Encapsulate(Quaternion.Euler(-angle, 0f, 0f) * corner);
                }
            bounds.Expand(new Vector3(.016f, .016f, .020f));
        }
        else
        {
            bounds.Expand(new Vector3(.008f, .008f, .012f));
            bounds.size = new Vector3(Mathf.Max(bounds.size.x, .06f), Mathf.Max(bounds.size.y, .024f), Mathf.Max(bounds.size.z, .015f));
        }
        Vector3 front = bounds.max;
        front.z = Mathf.Max(front.z, shellPlaneZ + .008f);
        bounds.SetMinMax(bounds.min, front);
        return bounds;
    }
}
