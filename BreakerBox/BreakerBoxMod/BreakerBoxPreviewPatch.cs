using HarmonyLib;
using Ships.Interface.Settings;
using Ships.Visuals;
using UnityEngine;

namespace BreakerBoxMod;
[HarmonyPatch(typeof(ShipPartInstancer), nameof(ShipPartInstancer.InstantiatePartPreview))]
public static class BreakerBoxPreviewPatch
{
    public static void Postfix(PartSettings partSettings, GameObject __result)
    {
        if (partSettings == null || partSettings.id != BreakerBoxMod.BreakerBoxPartId || __result == null)
            return;
        foreach (Collider collider in __result.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in __result.GetComponentsInChildren<Rigidbody>(true))
        {
            body.detectCollisions = false;
            body.isKinematic = true;
        }
    }
}
