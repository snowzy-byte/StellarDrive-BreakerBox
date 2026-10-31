using System;

namespace BreakerBoxMod;
public sealed class BreakerState
{
    public static readonly double[] Limits =
    {
        0,
        1,
        5,
        10,
        50,
        100
    };
    public bool Master = true;
    public readonly bool[] Channels =
    {
        true,
        true,
        true,
        true
    };
    public int Limit;
    public int Display;
    public void AdvanceLimit()
    {
        Limit = (Math.Max(0, Math.Min(Limits.Length - 1, Limit)) + 1) % Limits.Length;
        Display = 0;
    }

    public bool Conducts(int channel) => Master && Channels[channel];
    public void SetMaster(bool enabled)
    {
        Master = enabled;
        if (!enabled)
            DisarmAll();
    }

    public void DisarmAll()
    {
        for (int i = 0; i < Channels.Length; i++)
            Channels[i] = false;
    }

    public bool Protect(double[] inputs, double trip, ref bool previousTripHigh)
    {
        if (inputs == null || inputs.Length != 4)
            throw new ArgumentException("Four inputs required", nameof(inputs));
        bool changed = false;
        bool tripHigh = trip > 0 && !double.IsNaN(trip);
        if (tripHigh && !previousTripHigh)
        {
            for (int i = 0; i < 4; i++)
                changed |= Channels[i];
            DisarmAll();
        }

        previousTripHigh = tripHigh;
        int selected = Math.Max(0, Math.Min(Limits.Length - 1, Limit));
        if (Master && selected != 0)
        {
            for (int i = 0; i < 4; i++)
            {
                if (Channels[i] && (inputs[i] > Limits[selected] || double.IsNaN(inputs[i]) || double.IsInfinity(inputs[i])))
                {
                    Channels[i] = false;
                    changed = true;
                }
            }
        }

        return changed;
    }
}
