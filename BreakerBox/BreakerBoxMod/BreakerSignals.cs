using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SDModKit.Content;
using SDModKit.Game;
using Ships.Cables;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Services;
using Ships.Services;

namespace BreakerBoxMod;
public static class BreakerSignals
{
    public const ushort PartTypeId = 8888;
    private static readonly string[] ChannelKeys =
    {
        "channels0",
        "channels1",
        "channels2",
        "channels3"
    };
    private static readonly FieldInfo GroupField = AccessTools.Field(typeof(ConnectionsTracker), "SignalGroupMap");
    private static readonly FieldInfo ShipsField = AccessTools.Field(typeof(ConnectionsTracker), "Ships");
    private static readonly Dictionary<PartContext, bool> TripHigh = new Dictionary<PartContext, bool>();
    private static readonly Dictionary<PartContext, double[]> Readings = new Dictionary<PartContext, double[]>();
    private static readonly Dictionary<PartContext, Entry> Entries = new Dictionary<PartContext, Entry>();
    private static readonly List<PartContext> Removed = new List<PartContext>();
    private static ConnectionsServerTracker _lastTracker;
    private static BreakerSignalReader _reader;
    private static bool _installed;
    public static event Action<uint, ushort> StateChanged;
    public static void Install(HarmonyLib.Harmony harmony)
    {
        if (_installed)
            return;
        harmony.Patch(AccessTools.Method(typeof(ConnectionsServerTracker), "PrepareConnectionsBeforeTransfers"), prefix: new HarmonyMethod(typeof(BreakerSignals), nameof(BeforeTransfers)));
        harmony.Patch(AccessTools.Method(typeof(ConnectionsTracker), "GetConnectedCableIds"), postfix: new HarmonyMethod(typeof(BreakerSignals), nameof(IncludeTripCable)));
        _installed = true;
    }

    public static void Clear()
    {
        _reader?.Dispose();
        _reader = null;
        Entries.Clear();
        TripHigh.Clear();
        Readings.Clear();
        _lastTracker = null;
    }

    public static void Reset() => Clear();
    public static double[] ReadInputs(uint shipId, ushort partId) => GetReadings(shipId, partId);
    public static BreakerState ReadConfig(PartConfig config)
    {
        var state = new BreakerState
        {
            Master = config.GetBool("master", true),
            Limit = Math.Max(0, Math.Min(5, config.GetInt("limit", 0))),
            Display = config.GetInt("display", 0) == 1 ? 1 : 0,
            WrongWay = (byte)Math.Max(0, Math.Min(15, config.GetInt("wrongWay", 0)))
        };
        for (int i = 0; i < 4; i++)
            state.Channels[i] = config.GetBool(ChannelKeys[i], true);
        return state;
    }

    public static void WriteConfig(PartConfig config, BreakerState state)
    {
        config.Set("master", state.Master);
        config.Set("limit", Math.Max(0, Math.Min(5, state.Limit)));
        config.Set("display", state.Display == 1 ? 1 : 0);
        config.Set("wrongWay", state.WrongWay & 15);
        for (int i = 0; i < 4; i++)
            config.Set(ChannelKeys[i], state.Channels[i]);
    }

    public static double[] GetReadings(uint shipId, ushort partId)
    {
        return Readings.TryGetValue(new PartContext(shipId, partId), out var values) ? (double[])values.Clone() : new double[4];
    }

    private sealed class Entry
    {
        public uint ShipId;
        public ushort PartId;
        public PartConfig Config;
        public BreakerState State;
        public double[] Inputs = new double[4];
        public double Trip;
        public byte WrongWay;
        public int AppliedMask = -1;
    }

    private static void BeforeTransfers(ConnectionsServerTracker __instance)
    {
        var ships = GameRefs.Service<ShipsServerTracker>();
        var def = ContentRegistry.PartDef(PartTypeId);
        if (ships == null || def == null)
            return;
        if (_lastTracker != __instance)
        {
            Clear();
            _lastTracker = __instance;
            _reader = new BreakerSignalReader(__instance.SignalConnectionGroups, ships);
        }

        Removed.Clear();
        foreach (var pair in Entries)
        {
            Entry entry = pair.Value;
            if (!ships.TryGetStatefulPart(pair.Key, out var part) || part.Settings == null || part.Settings.id != PartTypeId)
            {
                Removed.Add(pair.Key);
                continue;
            }

            entry.State.Master = entry.Config.GetBool("master", true);
            entry.State.Limit = Math.Max(0, Math.Min(5, entry.Config.GetInt("limit", 0)));
            entry.State.Display = entry.Config.GetInt("display", 0);
            entry.State.WrongWay = (byte)Math.Max(0, Math.Min(15, entry.Config.GetInt("wrongWay", 0)));
            int mask = entry.State.Master ? 1 : 0;
            for (int i = 0; i < 4; i++)
            {
                entry.State.Channels[i] = entry.Config.GetBool(ChannelKeys[i], true);
                if (entry.State.Master && entry.State.Channels[i])
                    mask |= 2 << i;
            }

            if (mask != entry.AppliedMask)
            {
                ReconcileConnections(__instance, entry.ShipId, entry.PartId, entry.State);
                entry.AppliedMask = mask;
            }
        }

        foreach (var key in Removed)
        {
            Entries.Remove(key);
            TripHigh.Remove(key);
            Readings.Remove(key);
        }

        foreach (var entry in Entries.Values)
        {
            entry.WrongWay = 0;
            for (byte channel = 0; channel < 4; channel++)
            {
                entry.Inputs[channel] = _reader.Read(new EntityPartSocket(entry.ShipId, entry.PartId, channel));
                if (_reader.Read(new EntityPartSocket(entry.ShipId, entry.PartId, (byte)(channel + 4))) != 0)
                    entry.WrongWay |= (byte)(1 << channel);
            }
            entry.Trip = __instance.GetSignalValue(new EntityPartSocket(entry.ShipId, entry.PartId, 8));
        }

        foreach (var entry in Entries.Values)
        {
            var key = new PartContext(entry.ShipId, entry.PartId);
            TripHigh.TryGetValue(key, out bool high);
            bool changed = entry.State.ProtectDirection(entry.WrongWay);
            changed |= entry.State.Protect(entry.Inputs, entry.Trip, ref high);
            TripHigh[key] = high;
            Readings[key] = entry.Inputs;
            if (!changed)
                continue;
            WriteConfig(entry.Config, entry.State);
            ReconcileConnections(__instance, entry.ShipId, entry.PartId, entry.State);
            entry.AppliedMask = -1;
            StateChanged?.Invoke(entry.ShipId, entry.PartId);
        }
    }

    public static void Register(ContentPart part)
    {
        var key = new PartContext(part.ShipId, part.PartId);
        if (Entries.TryGetValue(key, out var existing))
        {
            existing.Config = part.Config;
            return;
        }

        Entries[key] = new Entry
        {
            ShipId = part.ShipId,
            PartId = part.PartId,
            Config = part.Config,
            State = new BreakerState()
        };
    }

    public static void Remove(uint shipId, ushort partId)
    {
        var key = new PartContext(shipId, partId);
        Entries.Remove(key);
        TripHigh.Remove(key);
        Readings.Remove(key);
    }

    public static void ReconcileConnections(ConnectionsTracker tracker, uint shipId, ushort partId, BreakerState state)
    {
        if (tracker == null || GroupField.GetValue(tracker)is not ConnectionGroupMap map)
            return;
        var ships = ShipsField.GetValue(tracker) as IShipsProvider;
        if (ships == null || !ships.TryGetStatefulPart(new PartContext(shipId, partId), out var part) || part.Settings == null || part.Settings.id != PartTypeId)
            return;
        for (byte channel = 0; channel < 4; channel++)
        {
            var input = new EntityPartSocket(shipId, partId, channel);
            var output = new EntityPartSocket(shipId, partId, (byte)(channel + 4));
            var edge = new SocketConnection(input, output, SocketConnectionType.RelayedByPart);
            var reverse = new SocketConnection(output, input, SocketConnectionType.RelayedByPart);
            bool forwardPresent = false, reversePresent = false;
            if (map.TryGetSocketConnections(input, out var connections))
            {
                foreach (var existing in connections)
                {
                    forwardPresent |= existing.Equals(edge);
                    reversePresent |= existing.Equals(reverse);
                }
            }

            if (!state.Conducts(channel))
            {
                if (forwardPresent)
                    tracker.DeleteConnection(edge, CableType.Signal);
                if (reversePresent)
                    tracker.DeleteConnection(reverse, CableType.Signal);
            }
            else if (!forwardPresent && !reversePresent)
                tracker.CreateConnection(edge, CableType.Signal);
        }
    }

    private static void IncludeTripCable(ConnectionsTracker __instance, PartContext partContext, ref IReadOnlyCollection<uint> __result)
    {
        var ships = ShipsField.GetValue(__instance) as IShipsProvider;
        if (ships == null || !ships.TryGetStatefulPart(partContext, out var part) || part.Settings == null || part.Settings.id != PartTypeId)
            return;
        var trip = new EntityPartSocket(partContext.ShipId, partContext.PartId, 8);
        if (!__instance.TryGetSocketCable(trip, CableType.Signal, out var cable) || __result.Contains(cable.CableId))
            return;
        var all = new List<uint>(__result)
        {
            cable.CableId
        };
        __result = all;
    }
}
