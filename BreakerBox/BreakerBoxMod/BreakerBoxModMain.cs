using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using SDModKit.Game;
using Ships.Cables.VisualObjects;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Settings;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.Common;
using Players.Interface.Model;
using System.Collections.Generic;
using UnityEngine;

[assembly: MelonInfo(typeof(BreakerBoxMod.BreakerBoxMod), "BreakerBox", "1.2.0", "Ghost", null)]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]
[assembly: MelonPriority(100)]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
namespace BreakerBoxMod;
public class BreakerBoxMod : MelonMod
{
    public const ushort BreakerBoxPartId = 8888;
    public static CustomPartDefinition Definition;
    public static readonly Vector3[] SocketInPositions = Enumerable.Range(0, 4).Select(BreakerBoxLayout.Input).ToArray();
    public static readonly Vector3[] SocketOutPositions = Enumerable.Range(0, 4).Select(BreakerBoxLayout.Output).ToArray();
    public static readonly Vector3 TripSocketOffset = BreakerBoxLayout.TripPosition;
    public override void OnInitializeMelon()
    {
        InstallSDModKitHelpers();
        BreakerBoxResearch.Install(HarmonyInstance);
        BreakerBoxRuntime.Install(HarmonyInstance);
        Definition = new CustomPartDefinition
        {
            Id = BreakerBoxPartId,
            Name = "BreakerBox",
            Donor = "SignalSplitter",
            BuildMenuAfter = "SignalSplitter",
            AutoFinish = false,
            Configure = ConfigureCustomPart
        };
        CustomParts.Register(Definition);
    }

    public override void OnUpdate()
    {
        BreakerBoxRuntime.Update();
    }

    public override void OnDeinitializeMelon()
    {
        BreakerBoxResearch.Remove();
    }

    public override void OnGUI()
    {
        if (!string.IsNullOrEmpty(BreakerBoxRuntime.HoverLabel))
            GUI.Box(new Rect(Screen.width / 2f - 130f, Screen.height / 2f + 45f, 260f, 28f), BreakerBoxRuntime.HoverLabel);
    }

    private static void InstallSDModKitHelpers()
    {
        Assembly assembly = typeof(CustomParts).Assembly;
        foreach (string name in new[]
        {
            "BuildPreview",
            "GhostSet",
            "PartLookup",
            "BuildCost",
            "IconOverrides",
            "PartGeometry",
            "RecycleGuard",
            "SocketPositions"
        }

        )
        {
            Type type = assembly.GetType("SDModKit.Game." + name);
            MethodInfo install = type?.GetMethod("Install", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            install?.Invoke(null, null);
        }
    }

    private static void ConfigureCustomPart(PartSettings settings, GameObject prefab)
    {
        BreakerBoxResearch.ConfigureCost(settings);
        IShipPartVisual visualReactor = prefab.GetComponentInChildren<IShipPartVisual>(true);
        if (visualReactor == null)
            throw new InvalidOperationException("SignalSplitter has no IShipPartVisual.");
        GameObject visualRoot = visualReactor.GameObject;
        SocketObject[] oldSockets = prefab.GetComponentsInChildren<SocketObject>(true);
        SocketObject template = oldSockets.FirstOrDefault(s => s.Type == CableType.Signal);
        if (template == null || template.visualObject == null || template.interactive == null)
            throw new InvalidOperationException("SignalSplitter has no complete signal socket template.");
        Material material = visualRoot.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null) ?? template.visualObject.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
        PartInteraction oldInteraction = prefab.GetComponentInChildren<PartInteraction>(true);
        int interactionLayer = oldInteraction != null ? oldInteraction.gameObject.layer : prefab.layer;
        var pickupLabels = oldInteraction?.Interactions?.Where(x => x.type == InteractionType.PickUp).ToList() ?? new List<PartInteractionConfig>();
        var created = new SocketObject[9];
        for (byte id = 0; id < 9; id++)
        {
            Vector3 position = id < 4 ? SocketInPositions[id] : id < 8 ? SocketOutPositions[id - 4] : TripSocketOffset;
            Vector3 inward = id < 4 ? Vector3.up : id < 8 ? Vector3.down : Vector3.right;
            Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
            created[id] = CreateSocket(template, prefab.transform, id, facing * position, facing * Quaternion.LookRotation(inward, Vector3.forward));
            foreach (Collider collider in created[id].visualObject.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in created[id].visualObject.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
        }

        foreach (SocketObject old in oldSockets)
        {
            DestroyDonorObject(old.visualObject, prefab, visualRoot);
            DestroyDonorObject(old.interactive, prefab, visualRoot);
            DestroyDonorObject(old.gameObject, prefab, visualRoot);
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old);
        }

        foreach (MeshRenderer renderer in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.GetComponentInParent<SocketObject>() == null)
                UnityEngine.Object.DestroyImmediate(renderer);
        foreach (MeshFilter filter in visualRoot.GetComponentsInChildren<MeshFilter>(true))
            if (filter.GetComponentInParent<SocketObject>() == null)
                UnityEngine.Object.DestroyImmediate(filter);
        foreach (MonoBehaviour component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            if (component is IShipPartVisual || component is IShipPartInteractions)
                UnityEngine.Object.DestroyImmediate(component);
        visualRoot = new GameObject("BreakerBoxPartVisual")
        {
            layer = prefab.layer
        };
        visualRoot.transform.SetParent(prefab.transform, false);
        visualRoot.AddComponent<DefaultShipPartVisuals>();
        GameObject model = BreakerBoxModel.CreateMeshVisual(material, "BreakerBoxVisual");
        model.transform.SetParent(visualRoot.transform, false);
        model.transform.localPosition = Vector3.up * BreakerBoxLayout.CenterY;
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = visualRoot.layer;
        settings.fullLabel = "Breaker Box";
        settings.description = "4 signal channels, master switch, remote trip and adjustable trip limit.";
        settings.localizedDescription = null;
        settings.mass = 5f;
        settings.snappingStyle = SnappingStyle.PreciseOnWall;
        settings.size = BreakerBoxLayout.Size;
        settings.thumbnailRotation = Quaternion.identity;
        ShipPartBounds bounds = prefab.GetComponent<ShipPartBounds>() ?? prefab.AddComponent<ShipPartBounds>();
        bounds.center = BreakerBoxLayout.BoundsCenter;
        bounds.bounds = BreakerBoxLayout.BoundsSize;
        GameObject interactionRoot = new GameObject("BreakerBoxPartInteractions")
        {
            layer = interactionLayer,
            tag = "Interactable"
        };
        interactionRoot.transform.SetParent(prefab.transform, false);
        interactionRoot.AddComponent<DefaultShipPartInteractions>();
        PartInteraction pickup = interactionRoot.AddComponent<PartInteraction>();
        Traverse.Create(pickup).Field("interactions").SetValue(pickupLabels);
        BoxCollider box = interactionRoot.AddComponent<BoxCollider>();
        box.center = new Vector3(BreakerBoxLayout.BoundsCenter.x, BreakerBoxLayout.CenterY, -BreakerBoxLayout.BodyDepth / 2f);
        box.size = new Vector3(BreakerBoxLayout.BoundsSize.x, BreakerBoxLayout.BoundsSize.y, BreakerBoxLayout.BodyDepth);
        BreakerBoxRuntime.Configure(settings, prefab);
        if (visualRoot.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException("BreakerBox visual must not contain colliders");
    }

    private static void DestroyDonorObject(GameObject obj, GameObject prefab, GameObject visualRoot)
    {
        if (obj == null || obj == prefab || obj == visualRoot)
            return;
        if (visualRoot.transform.IsChildOf(obj.transform))
            return;
        if (obj.GetComponentsInChildren<IShipPartInteractions>(true).Length != 0)
            return;
        UnityEngine.Object.DestroyImmediate(obj);
    }

    private static SocketObject CreateSocket(SocketObject donor, Transform parent, byte id, Vector3 position, Quaternion rotation)
    {
        GameObject copy = new GameObject("BreakerBoxSocket_" + id)
        {
            layer = donor.gameObject.layer
        };
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = position;
        copy.transform.localRotation = rotation;
        copy.transform.localScale = Vector3.one;
        SocketObject socket = copy.AddComponent<SocketObject>();
        Traverse.Create(socket).Field("id").SetValue(id);
        Traverse.Create(socket).Field("type").SetValue(CableType.Signal);
        Traverse.Create(socket).Field("relayedSockets").SetValue(Array.Empty<SocketObject>());
        socket.visualObject = UnityEngine.Object.Instantiate(donor.visualObject, copy.transform, false);
        socket.interactive = UnityEngine.Object.Instantiate(donor.interactive, copy.transform, false);
        foreach (SocketObject nested in socket.visualObject.GetComponentsInChildren<SocketObject>(true))
            UnityEngine.Object.DestroyImmediate(nested);
        foreach (SocketObject nested in socket.interactive.GetComponentsInChildren<SocketObject>(true))
            UnityEngine.Object.DestroyImmediate(nested);
        foreach (SocketInteractionObject interaction in socket.interactive.GetComponentsInChildren<SocketInteractionObject>(true))
            interaction.cableType = CableType.Signal;
        socket.visualObject.transform.localPosition = Vector3.zero;
        socket.visualObject.transform.localRotation = Quaternion.identity;
        socket.interactive.transform.localPosition = Vector3.zero;
        socket.interactive.transform.localRotation = Quaternion.identity;
        return socket;
    }
}
