using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Services;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Players;
using Players.Interface.Model;
using SDModKit.Content;
using SDModKit.Game;
using Ships;
using Ships.Cables;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Interface.Settings;
using Ships.Parts.Common;
using Ships.Parts.Common.Model;
using Ships.Parts.Common.Model.Reactions;
using Ships.Parts.Common.Utils;
using Ships.Services;
using UnityEngine;
using WorldTracking.Interface.Services;

namespace BreakerBoxMod;
public static class BreakerBoxRuntime
{
    public struct Command : IBroadcast
    {
        public uint Ship;
        public ushort Part;
        public byte Control;
        public uint Sequence;
    }

    public struct Snapshot : IBroadcast
    {
        public uint Ship;
        public ushort Part;
        public uint Revision;
        public byte Flags, Limit, Display, WrongWay;
        public double A, B, C, D;
        public double[] Inputs => new[]
        {
            A,
            B,
            C,
            D
        };

        public BreakerState State
        {
            get
            {
                var state = new BreakerState
                {
                    Master = (Flags & 1) != 0,
                    Limit = Limit,
                    Display = Display,
                    WrongWay = WrongWay
                };
                for (int i = 0; i < 4; i++)
                    state.Channels[i] = (Flags & (2 << i)) != 0;
                return state;
            }
        }
    }

    private static NetworkManager _network;
    private static readonly Dictionary<PartContext, Snapshot> Snapshots = new Dictionary<PartContext, Snapshot>();
    private static readonly Dictionary<PartContext, float> NextPublish = new Dictionary<PartContext, float>();
    private static readonly Dictionary<TrackedPlayerServer, uint> LastCommand = new Dictionary<TrackedPlayerServer, uint>();
    private static uint _commandSequence, _revision;
    private static readonly ContentPack Pack = new ContentPack
    {
        Id = "breakerbox",
        Name = "Breaker Box",
        Version = "1.3.0",
        Assembly = typeof(BreakerBoxRuntime).Assembly
    };
    public static string HoverLabel;
    public static void Install(HarmonyLib.Harmony harmony)
    {
        ContentRegistry.Behaviour("breakerbox.runtime", () => new Behaviour());
        BreakerSignals.Install(harmony);
        GenericWriter<Command>.SetWrite((w, m) =>
        {
            w.WriteUInt32(m.Ship);
            w.WriteUInt16(m.Part);
            w.WriteUInt8Unpacked(m.Control);
            w.WriteUInt32(m.Sequence);
        });
        GenericReader<Command>.SetRead(r => new Command { Ship = r.ReadUInt32(), Part = r.ReadUInt16(), Control = r.ReadUInt8Unpacked(), Sequence = r.ReadUInt32() });
        GenericWriter<Snapshot>.SetWrite((w, m) =>
        {
            w.WriteUInt32(m.Ship);
            w.WriteUInt16(m.Part);
            w.WriteUInt32(m.Revision);
            w.WriteUInt8Unpacked(m.Flags);
            w.WriteUInt8Unpacked(m.Limit);
            w.WriteUInt8Unpacked(m.Display);
            w.WriteUInt8Unpacked(m.WrongWay);
            w.WriteDouble(m.A);
            w.WriteDouble(m.B);
            w.WriteDouble(m.C);
            w.WriteDouble(m.D);
        });
        GenericReader<Snapshot>.SetRead(r => new Snapshot { Ship = r.ReadUInt32(), Part = r.ReadUInt16(), Revision = r.ReadUInt32(), Flags = r.ReadUInt8Unpacked(), Limit = r.ReadUInt8Unpacked(), Display = r.ReadUInt8Unpacked(), WrongWay = r.ReadUInt8Unpacked(), A = r.ReadDouble(), B = r.ReadDouble(), C = r.ReadDouble(), D = r.ReadDouble() });
    }

    public static void Configure(PartSettings settings, GameObject prefab)
    {
        ContentRegistry.RegisterAttached(new ContentAttachDef { Pack = Pack, Id = "breakerbox", Socket = "signal", Menu = "none", Behaviour = "breakerbox.runtime", Config = new JObject() }, settings, Enumerable.Range(0, 9).Select(i => (byte)i));
        var visual = prefab.GetComponentInChildren<IShipPartVisual>(true).GameObject;
        if (visual.GetComponent<BreakerBoxView>() == null)
            visual.AddComponent<BreakerBoxView>();
        var interaction = prefab.GetComponentInChildren<IShipPartInteractions>(true);
        if (interaction == null)
            throw new InvalidOperationException("Donor has no interaction root");
        GameObject interactionRoot = interaction.GameObject;
        var donor = interactionRoot.GetComponentInChildren<PartInteraction>(true);
        var label = donor != null ? donor.Interactions.FirstOrDefault() : default;
        label.type = InteractionType.Interact;
        label.localizedLabel = null;
        string[] names =
        {
            "Lever_Master",
            "Lever_1",
            "Lever_2",
            "Lever_3",
            "Lever_4",
            "Led_Master",
            "Screen_Panel"
        };
        for (byte i = 0; i < names.Length; i++)
        {
            if (names[i] == null)
                continue;
            var group = visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == names[i]);
            if (group == null)
                throw new InvalidOperationException("Breaker Box control missing: " + names[i]);
            var mesh = group.GetComponentInChildren<MeshFilter>();
            var localBounds = mesh != null ? mesh.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.04f);
            var control = new GameObject("BreakerControl_" + i);
            control.transform.SetParent(interactionRoot.transform, false);
            control.transform.localRotation = Quaternion.Inverse(interactionRoot.transform.rotation) * group.rotation;
            control.layer = donor != null ? donor.gameObject.layer : interactionRoot.layer;
            control.tag = "Interactable";
            var box = control.AddComponent<BoxCollider>();
            Vector3 shell = prefab.transform.TransformPoint(new Vector3(0f, 0f, -BreakerBoxLayout.BodyDepth));
            Bounds hitBounds = BreakerBoxHitBounds.Create(localBounds, i < 5, group.InverseTransformPoint(shell).z);
            control.transform.position = group.TransformPoint(hitBounds.center);
            Vector3 size = Vector3.Scale(hitBounds.size, group.lossyScale);
            Vector3 scale = interactionRoot.transform.lossyScale;
            box.size = new Vector3(Mathf.Abs(size.x / scale.x), Mathf.Abs(size.y / scale.y), Mathf.Abs(size.z / scale.z));
            var component = control.AddComponent<BreakerBoxControl>();
            component.Control = i;
            component.Labels = new[]
            {
                label
            };
        }
    }

    public static void Update()
    {
        NetworkManager current = InstanceFinder.NetworkManager;
        if (current == _network)
            return;
        if (_network != null)
        {
            _network.ServerManager.UnregisterBroadcast<Command>(ReceiveCommand);
            _network.ClientManager.UnregisterBroadcast<Snapshot>(ReceiveSnapshot);
        }

        Reset();
        _network = current;
        if (_network == null)
            return;
        _network.ServerManager.RegisterBroadcast<Command>(ReceiveCommand, true);
        _network.ClientManager.RegisterBroadcast<Snapshot>(ReceiveSnapshot);
    }

    public static void Reset()
    {
        Snapshots.Clear();
        NextPublish.Clear();
        LastCommand.Clear();
        _commandSequence = 0;
        _revision = 0;
        HoverLabel = null;
        BreakerSignals.Clear();
    }

    public static bool TrySnapshot(PartContext part, out Snapshot snapshot) => Snapshots.TryGetValue(part, out snapshot);
    public static void Send(PartContext part, byte control)
    {
        Update();
        if (_network == null)
            return;
        var message = new Command
        {
            Ship = part.ShipId,
            Part = part.PartId,
            Control = control,
            Sequence = ++_commandSequence
        };
        if (_network.IsServerStarted)
            Apply(PlayerAccess.LocalServerPlayer, message, null);
        else if (_network.IsClientStarted)
            _network.ClientManager.Broadcast(message, Channel.Reliable);
    }

    private static void ReceiveCommand(NetworkConnection connection, Command message, Channel channel)
    {
        if (connection == null || GameRefs.PlayersServer == null)
            return;
        var player = GameRefs.PlayersServer.ActivePlayers.FirstOrDefault(p => p.ConnectionId == connection.ClientId);
        Apply(player, message, connection);
    }

    private static void Apply(TrackedPlayerServer player, Command message, NetworkConnection connection)
    {
        if (player == null || (message.Control > 6 && message.Control != 255))
            return;
        var ships = GameRefs.Service<ShipsServerTracker>();
        var ship = ships == null ? null : ships.GetShip(message.Ship);
        if (ship == null || !ContentRuntime.TryResolve(ship, message.Part, out var part) || part.Def.Id != 8888)
            return;
        var transpose = ServiceLocator.GetService<IWorldTransposeServer>();
        if (transpose == null)
            return;
        Vector3 playerPos = transpose.Position(player.SpaceId, ship.RelativeSpaceId, player.Position);
        float maxDistance = message.Control == 255 ? 100f : 3f;
        if ((playerPos - part.LocalPosition).sqrMagnitude > maxDistance * maxDistance)
            return;
        if (message.Control == 255)
        {
            Publish(part, connection, true);
            return;
        }

        if (player.StateVal != PlayerStateType.Walking && player.StateVal != PlayerStateType.CheatFlying)
            return;
        if (LastCommand.TryGetValue(player, out uint previousSequence) && message.Sequence <= previousSequence)
            return;
        LastCommand[player] = message.Sequence;
        var state = BreakerSignals.ReadConfig(part.Config);
        if (message.Control == 0)
            state.SetMaster(!state.Master);
        else if (message.Control <= 4)
            state.ToggleChannel(message.Control - 1);
        else if (message.Control == 5)
            state.Display = 1 - state.Display;
        else
            state.AdvanceLimit();
        BreakerSignals.WriteConfig(part.Config, state);
        BreakerSignals.ReconcileConnections(GameRefs.Service<ConnectionsServerTracker>(), part.ShipId, part.PartId, state);
        Publish(part, null, true);
    }

    private static void ReceiveSnapshot(Snapshot snapshot, Channel channel)
    {
        if (_network != null && _network.IsServerStarted)
            return;
        StoreSnapshot(snapshot);
    }

    private static void StoreSnapshot(Snapshot snapshot)
    {
        if (snapshot.Limit > 5 || snapshot.Display > 1 || snapshot.WrongWay > 15)
            return;
        var key = new PartContext(snapshot.Ship, snapshot.Part);
        if (Snapshots.TryGetValue(key, out var current) && snapshot.Revision <= current.Revision)
            return;
        Snapshots[new PartContext(snapshot.Ship, snapshot.Part)] = snapshot;
        if (_network == null || !_network.IsClientStarted)
            return;
        var tracker = GameRefs.Service<ConnectionsClientTracker>();
        var ships = GameRefs.Service<ShipsClientTracker>();
        if (ships != null && ships.TryGetShip(snapshot.Ship, out IShipStateRead ship) && ship.TryGetStatefulPart(snapshot.Part, out var part) && part.Settings != null && part.Settings.id == 8888)
            BreakerSignals.ReconcileConnections(tracker, snapshot.Ship, snapshot.Part, snapshot.State);
    }

    private static void Publish(ContentPart part, NetworkConnection connection = null, bool force = false)
    {
        if (_network == null || !_network.IsServerStarted)
            return;
        var key = new PartContext(part.ShipId, part.PartId);
        if (!force && NextPublish.TryGetValue(key, out float next) && Time.unscaledTime < next)
            return;
        NextPublish[key] = Time.unscaledTime + 0.2f;
        var state = BreakerSignals.ReadConfig(part.Config);
        var input = BreakerSignals.GetReadings(part.ShipId, part.PartId);
        byte flags = (byte)(state.Master ? 1 : 0);
        for (int i = 0; i < 4; i++)
            if (state.Channels[i])
                flags |= (byte)(2 << i);
        var snapshot = new Snapshot
        {
            Ship = part.ShipId,
            Part = part.PartId,
            Revision = ++_revision,
            Flags = flags,
            Limit = (byte)state.Limit,
            Display = (byte)state.Display,
            WrongWay = state.WrongWay,
            A = input[0],
            B = input[1],
            C = input[2],
            D = input[3]
        };
        bool changed = !Snapshots.TryGetValue(key, out var previous) || (snapshot.Flags != previous.Flags || snapshot.Limit != previous.Limit || snapshot.Display != previous.Display || snapshot.WrongWay != previous.WrongWay || snapshot.A != previous.A || snapshot.B != previous.B || snapshot.C != previous.C || snapshot.D != previous.D);
        StoreSnapshot(snapshot);
        if (connection != null)
            _network.ServerManager.Broadcast(connection, snapshot, true, Channel.Reliable);
        else if (force || changed)
            _network.ServerManager.Broadcast(snapshot, true, Channel.Reliable);
    }

    private sealed class Behaviour : PartBehaviour
    {
        public override void OnWorldEnter() => Reset();
        public override void OnWorldExit() => Reset();
        public override void OnPartAdded(ContentPart part) => BreakerSignals.Register(part);
        public override void ServerTick(ContentPart part, float deltaTime)
        {
            BreakerSignals.Register(part);
            Publish(part);
        }

        public override void OnPartRemoved(uint shipId, ushort partId)
        {
            var key = new PartContext(shipId, partId);
            Snapshots.Remove(key);
            NextPublish.Remove(key);
            BreakerSignals.Remove(shipId, partId);
        }
    }
}

public sealed class BreakerBoxView : MonoBehaviour, IContextAwarePart
{
    private PartContext _part;
    private bool _ready;
    private float _nextRequest, _nextVisual;
    private BreakerBoxIndicators _indicators;
    public void SetContext(ShipPartContext context)
    {
        _part = context.Part;
        _ready = true;
    }

    private void Update()
    {
        if (!_ready)
            return;
        if (Time.unscaledTime < _nextVisual)
            return;
        _nextVisual = Time.unscaledTime + 0.1f;
        bool hasSnapshot = BreakerBoxRuntime.TrySnapshot(_part, out var snapshot);
        if (!hasSnapshot && Time.unscaledTime >= _nextRequest)
        {
            BreakerBoxRuntime.Send(_part, 255);
            _nextRequest = Time.unscaledTime + 2f;
        }

        if (!hasSnapshot)
            return;
        if (_indicators == null)
            _indicators = GetComponentInChildren<BreakerBoxIndicators>();
        if (_indicators != null)
            _indicators.Apply(snapshot);
    }
}

public sealed class BreakerBoxControl : MonoBehaviour, IInteractableObject, IContextAwarePart
{
    public byte Control;
    public PartInteractionConfig[] Labels;
    private PartContext _part;
    private bool _ready;
    private int _lastClickFrame = -1;
    public bool IsActive => _ready;
    public float DetectionDistance => 2.5f;
    public IReadOnlyCollection<PartInteractionConfig> Interactions => Labels;

    public void SetContext(ShipPartContext context)
    {
        _part = context.Part;
        _ready = true;
    }

    public bool Interact(InteractionType type)
    {
        if (!_ready || type != InteractionType.Interact)
            return false;
        if (_lastClickFrame == Time.frameCount)
            return true;
        _lastClickFrame = Time.frameCount;
        BreakerBoxRuntime.Send(_part, Control);
        return true;
    }

    public void EnableHover() => BreakerBoxRuntime.HoverLabel = Control == 0 ? "Toggle master switch" : Control <= 4 ? "Toggle breaker " + Control : Control == 5 ? "Display: limit / signal" : "Change trip limit";
    public void DisableHover() => BreakerBoxRuntime.HoverLabel = null;
    private void OnDisable() => DisableHover();
}
