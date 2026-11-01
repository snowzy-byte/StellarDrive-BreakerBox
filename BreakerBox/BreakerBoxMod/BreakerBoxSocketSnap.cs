using System;
using System.Collections.Generic;
using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxSocketSnap
{
    public struct Port
    {
        public Vector3 Position;
        public Vector3 Inward;
        public bool Breaker;
    }

    public static bool TryShift(IReadOnlyList<Port> source, IReadOnlyList<Port> targets,
        Vector3 normal, Func<Vector3, bool> canPlace, out Vector3 shift)
    {
        shift = Vector3.zero;
        int bestCount = 0;
        float bestDistance = float.PositiveInfinity;
        foreach (Port a in source)
            foreach (Port b in targets)
            {
                if (!a.Breaker && !b.Breaker || Vector3.Dot(a.Inward, b.Inward) > -.98f)
                    continue;
                Vector3 delta = b.Position - a.Position;
                float depth = Vector3.Dot(delta, normal);
                if (Math.Abs(depth) > .005f)
                    continue;
                delta -= normal * depth;
                float distance = delta.sqrMagnitude;
                if (distance > .18f * .18f)
                    continue;
                int count = 0;
                foreach (Port p in source)
                    foreach (Port q in targets)
                        if ((p.Breaker || q.Breaker) && Vector3.Dot(p.Inward, q.Inward) < -.98f
                            && (p.Position + delta - q.Position).sqrMagnitude < .003f * .003f)
                            count++;
                if (count < bestCount || count == bestCount && distance >= bestDistance || !canPlace(delta))
                    continue;
                bestCount = count;
                bestDistance = distance;
                shift = delta;
            }
        return bestCount > 0 && bestDistance > .000001f;
    }
}
