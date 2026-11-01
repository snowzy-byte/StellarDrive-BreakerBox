using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Items.Model;
using MelonLoader;
using Research;
using Research.Interface.Model;
using Research.Interface.Values;
using SDModKit.Content;
using SDModKit.Game;
using Ships.Interface.Settings;
using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxResearch
{
    public const ushort ResearchId = 8888;
    public const int MaterialQuantity = 6;
    private static readonly FieldInfo BuildingCost = AccessTools.Field(typeof(PartSettings), "buildingCost");
    private static readonly Dictionary<ResearchSettingsList, ResearchSettings> Registered = new Dictionary<ResearchSettingsList, ResearchSettings>();
    private static bool _registering;
    private static bool _reportedConflict;

    public static void Install(HarmonyLib.Harmony harmony)
    {
        var clientPrefix = new HarmonyMethod(typeof(BreakerBoxResearch), nameof(BeforeClientLookup));
        harmony.Patch(AccessTools.Method(typeof(ResearchClientProxy), "Awake"), prefix: clientPrefix);
        harmony.Patch(AccessTools.PropertyGetter(typeof(ResearchClientProxy), nameof(ResearchClientProxy.Researches)), prefix: clientPrefix);
        harmony.Patch(AccessTools.Method(typeof(ResearchClientProxy), nameof(ResearchClientProxy.IsPartUnlocked)), prefix: clientPrefix);
        harmony.Patch(AccessTools.Method(typeof(ResearchSettingsList), nameof(ResearchSettingsList.GetResearchSettingById)),
            prefix: new HarmonyMethod(typeof(BreakerBoxResearch), nameof(BeforeResearchLookup)));
        harmony.Patch(AccessTools.Method(typeof(ResearchSettingsList), nameof(ResearchSettingsList.GetTechsRequiredForPart)),
            prefix: new HarmonyMethod(typeof(BreakerBoxResearch), nameof(BeforeResearchLookup)));
    }

    public static void ConfigureCost(PartSettings settings)
    {
        BuildingCost.SetValue(settings, new List<ItemInstance>(CreateCost()));
    }

    private static ItemInstance[] CreateCost()
    {
        ItemSettings copper = FindItem(ItemCatalog.All, "Copper");
        ItemSettings iron = ItemCatalog.ById(ItemsConstants.IronItemId);
        if (copper == null || iron == null)
        {
            ItemSettings[] loaded = Resources.FindObjectsOfTypeAll<ItemSettings>();
            if (copper == null)
                copper = FindItem(loaded, "Copper");
            if (iron == null)
                iron = loaded.FirstOrDefault(item => item != null && item.id == ItemsConstants.IronItemId);
        }
        if (copper == null || iron == null)
            throw new InvalidOperationException("Cannot find Copper and Iron for the Breaker Box cost.");
        return new[] { new ItemInstance(copper, MaterialQuantity), new ItemInstance(iron, MaterialQuantity) };
    }

    private static ItemSettings FindItem(IEnumerable<ItemSettings> items, string name)
    {
        return items.FirstOrDefault(item => item != null &&
            (string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.itemName, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static void BeforeClientLookup(ResearchSettingsList ___researchSettingsList)
    {
        EnsureRegistered(___researchSettingsList);
    }

    private static void BeforeResearchLookup(ResearchSettingsList __instance)
    {
        EnsureRegistered(__instance);
    }

    private static void EnsureRegistered(ResearchSettingsList list)
    {
        if (list == null || list.list == null || _registering)
            return;
        if (Registered.TryGetValue(list, out ResearchSettings existing))
        {
            if (existing != null && list.list.Contains(existing))
                return;
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
            Registered.Remove(list);
        }
        _registering = true;
        try
        {
            PartSettings part = CustomParts.Get(BreakerBoxMod.BreakerBoxPartId);
            if (part == null)
                return;
            if (list.list.Any(research => research != null && research.id == ResearchId))
            {
                if (!_reportedConflict)
                {
                    MelonLogger.Warning("Breaker Box research ID 8888 is already in use; research was not registered.");
                    _reportedConflict = true;
                }
                return;
            }
            ResearchSettings prerequisite = list.list.FirstOrDefault(research => research != null &&
                research.availableInDemo && !research.hideFromTechTree && research.unlockedParts != null &&
                research.unlockedParts.Any(unlocked => unlocked != null && unlocked.name == "SignalSplitter"));
            if (prerequisite == null)
                return;
            ItemInstance[] cost = CreateCost();
            ResearchSettings research = ScriptableObject.CreateInstance<ResearchSettings>();
            research.name = "BreakerBoxResearch";
            research.hideFlags = HideFlags.DontSave;
            research.id = ResearchId;
            research.techName = "Breaker Box";
            research.unlockedParts = new[] { part };
            research.unlockedFeatures = Array.Empty<ResearchUnlockableFeature>();
            research.unlockedGuideEntriesIds = Array.Empty<uint>();
            research.requirements = new[] { prerequisite };
            research.resourceCost = cost;
            research.positionInTechTree = FindPosition(list.list, prerequisite);
            research.hideFromTechTree = false;
            research.availableInDemo = true;
            research.exclusiveToDemo = false;
            list.list.Add(research);
            Registered[list] = research;
            ResearchLayout.Settle(list);
        }
        finally
        {
            _registering = false;
        }
    }

    private static Vector2Int FindPosition(IEnumerable<ResearchSettings> researches, ResearchSettings prerequisite)
    {
        ResearchSettings[] entries = researches.Where(research => research != null).ToArray();
        int row = prerequisite.positionInTechTree.y > 0 ? prerequisite.positionInTechTree.y - 1 : 1;
        Vector2Int position = new Vector2Int(prerequisite.positionInTechTree.x + 1, row);
        while (entries.Any(research => research.positionInTechTree == position))
            position.x++;
        return position;
    }

    public static void Remove()
    {
        foreach (var entry in Registered)
        {
            if (entry.Key != null && entry.Key.list != null)
                entry.Key.list.Remove(entry.Value);
            if (entry.Value != null)
                UnityEngine.Object.Destroy(entry.Value);
        }
        Registered.Clear();
        _reportedConflict = false;
    }
}
