using System.Collections.Generic;
using HarmonyLib;
using SDModKit.Game;
using Ships.Cables;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Placement;
using Ships.Interface.Model.State;
using Ships.Interface.Settings;
using Ships.Services;
using Tools.Build;
using Tools.Build.Model;
using UnityEngine;

namespace BreakerBoxMod;
[HarmonyPatch(typeof(PartPreviewDetection), nameof(PartPreviewDetection.CalculateSnapDetectionUpdate))]
public static class BreakerBoxPlacementPatch
{
    private struct LocalPort
    {
        public byte Id;
        public Vector3 Position;
        public Vector3 Inward;
    }
    private static readonly Dictionary<PartSettings, LocalPort[]> Cache = new Dictionary<PartSettings, LocalPort[]>();

    public static void Postfix(SnapDetectionState state, ref SnapDetectionResults __result)
    {
        if (!__result.IsSnapped || __result.shipPartsProxy == null || state.partSettings == null
            || !(__result.placement is FloatingPartPlacement placement))
            return;
        var ships = GameRefs.Service<ShipsClientTracker>();
        if (ships == null || !ships.TryGetShip(__result.shipPartsProxy.ShipId, out IShipStateRead ship))
            return;
        bool breaker = state.partSettings.id == BreakerBoxMod.BreakerBoxPartId;
        var source = new List<BreakerBoxSocketSnap.Port>();
        foreach (LocalPort port in Ports(state.partSettings))
            source.Add(World(port, placement.Coord, placement.Rotation, breaker));
        if (source.Count == 0) return;
        var targets = new List<BreakerBoxSocketSnap.Port>();
        var connections = GameRefs.Service<ConnectionsClientTracker>();
        foreach (StatefulPart part in ship.StatefulParts)
        {
            bool otherBreaker = part.Settings.id == BreakerBoxMod.BreakerBoxPartId;
            if (!breaker && !otherBreaker || (part.LocalPosition - placement.Coord).sqrMagnitude > 2.25f)
                continue;
            foreach (LocalPort port in Ports(part.Settings))
                if (connections == null || !connections.IsSocketPhysicallyOccupied(new EntityPartSocket(ship.Id, part.Id, port.Id)))
                    targets.Add(World(port, part.LocalPosition, part.LocalRotation, otherBreaker));
        }
        var grid = __result.shipPartsProxy;
        Vector3 normal = breaker ? -(placement.Rotation * Vector3.forward) : placement.Rotation * Vector3.up;
        if (!BreakerBoxSocketSnap.TryShift(source, targets, normal, delta =>
            grid.IsFloatingPlacementFree(new FloatingPartPlacement(placement.Coord + delta,
                placement.ForwardOrientation, placement.UpOrientation), state.partSettings.Bounds), out Vector3 shift))
            return;
        placement.Coord += shift;
        __result.placement = placement;
        __result.pos = grid.ToWorldPos(placement);
    }

    private static BreakerBoxSocketSnap.Port World(LocalPort port, Vector3 position, Quaternion rotation, bool breaker) =>
        new BreakerBoxSocketSnap.Port { Position = position + rotation * port.Position, Inward = rotation * port.Inward, Breaker = breaker };

    private static LocalPort[] Ports(PartSettings settings)
    {
        if (Cache.TryGetValue(settings, out LocalPort[] ports)) return ports;
        var result = new List<LocalPort>();
        Transform root = settings.part.transform;
        foreach (SocketObject socket in settings.GetSockets())
            if (socket.Type == CableType.Signal)
                result.Add(new LocalPort { Id = socket.Id, Position = root.InverseTransformPoint(socket.transform.position),
                    Inward = root.InverseTransformDirection(socket.transform.forward) });
        ports = result.ToArray();
        Cache[settings] = ports;
        return ports;
    }
}
