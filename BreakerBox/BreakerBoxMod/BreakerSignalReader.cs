using System;
using System.Collections.Generic;
using Ships.Cables;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Services;

namespace BreakerBoxMod;
public sealed class BreakerSignalReader : IDisposable
{
    private readonly ConnectionGroupMap _map;
    private readonly IShipsProvider _ships;
    private readonly SignalConnectionGroupValues _rawValues;
    private readonly Dictionary<EntityPartSocket, List<EntityPartSocket>> _sources = new Dictionary<EntityPartSocket, List<EntityPartSocket>>();
    private readonly Queue<EntityPartSocket> _pending = new Queue<EntityPartSocket>();
    private readonly HashSet<EntityPartSocket> _visited = new HashSet<EntityPartSocket>();

    public BreakerSignalReader(ConnectionGroupMap map, IShipsProvider ships)
    {
        _map = map;
        _ships = ships;
        _rawValues = new SignalConnectionGroupValues(new ConnectionGroupMap(), ships);
        _map.OnConnectionGroupUpdated += Invalidate;
    }

    public double Read(EntityPartSocket start)
    {
        if (!_sources.TryGetValue(start, out var sources))
        {
            sources = FindSources(start);
            _sources[start] = sources;
        }
        double value = 0;
        foreach (EntityPartSocket source in sources)
        {
            double signal = _rawValues.GetSocketSignalValue(source);
            if (double.IsNaN(signal)) return signal;
            value = SignalCablesUtils.ResolveConflictingSignal(value, signal);
        }
        return value;
    }

    private List<EntityPartSocket> FindSources(EntityPartSocket start)
    {
        var sources = new List<EntityPartSocket>();
        _pending.Clear();
        _visited.Clear();
        _pending.Enqueue(start);
        while (_pending.Count != 0)
        {
            EntityPartSocket current = _pending.Dequeue();
            if (!_visited.Add(current)) continue;
            if (_ships.TryGetStatefulPart(current.PartContext, out var part))
            {
                byte id = current.Socket.SocketId;
                if (part.State is ISignalEmitterState emitter && Array.IndexOf(emitter.EmissionSockets, id) >= 0
                    || part.State is ISignalProcessorState processor && Array.IndexOf(processor.EmissionSockets, id) >= 0)
                    sources.Add(current);
            }
            if (!_map.TryGetSocketConnections(current, out var edges)) continue;
            foreach (SocketConnection edge in edges)
            {
                EntityPartSocket? other = edge.SocketA.HasValue && edge.SocketA.Value.Equals(current) ? edge.SocketB : edge.SocketA;
                if (!other.HasValue || other.Value.Equals(current)) continue;
                if (edge.Type == SocketConnectionType.RelayedByPart && current.PartContext.Equals(other.Value.PartContext)
                    && _ships.TryGetStatefulPart(current.PartContext, out var relay) && relay.Settings.id == BreakerSignals.PartTypeId)
                {
                    if (current.PartContext.Equals(start.PartContext) || current.Socket.SocketId < 4 || current.Socket.SocketId > 7
                        || other.Value.Socket.SocketId != current.Socket.SocketId - 4)
                        continue;
                }
                _pending.Enqueue(other.Value);
            }
        }
        return sources;
    }

    private void Invalidate(uint groupId) => _sources.Clear();

    public void Dispose()
    {
        _map.OnConnectionGroupUpdated -= Invalidate;
        _sources.Clear();
    }
}
