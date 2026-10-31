using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Research.Interface.Services;
using Ships.Interface.Settings;
using UnityEngine;

namespace BreakerBoxMod;
[HarmonyPatch]
public static class ResearchProvider_IsPartUnlocked_Patch
{
    public const ushort BreakerBoxPartId = 8888;
    public static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in AccessTools.AllTypes())
        {
            if (!typeof(IResearchProvider).IsAssignableFrom(type))
                continue;
            if (type.IsInterface || type.IsAbstract)
                continue;
            MethodInfo method = AccessTools.Method(type, "IsPartUnlocked", new Type[1] { typeof(PartSettings) }, (Type[])null);
            if (method == null)
                continue;
            if (method.IsAbstract || !method.HasMethodBody())
                continue;
            yield return method;
        }
    }

    public static void Postfix(PartSettings part, ref bool __result)
    {
        if ((UnityEngine.Object)(object)part != (UnityEngine.Object)null && part.id == BreakerBoxPartId)
        {
            __result = true;
        }
    }
}
