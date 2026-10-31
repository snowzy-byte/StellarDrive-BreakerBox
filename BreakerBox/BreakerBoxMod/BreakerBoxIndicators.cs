using System.Globalization;
using TMPro;
using UnityEngine;

namespace BreakerBoxMod;
public sealed class BreakerBoxIndicators : MonoBehaviour
{
    private readonly Transform[] _levers = new Transform[5];
    private readonly Renderer[] _leds = new Renderer[5];
    private readonly Quaternion[] _targets = new Quaternion[5];
    private readonly int[] _colors =
    {
        -1,
        -1,
        -1,
        -1,
        -1
    };
    private MaterialPropertyBlock _properties;
    private TMP_Text _text;
    private bool _moving;
    private int _flags = -1, _display = -1, _limit = -1;
    private double _shownSignal = double.NaN;
    private void Awake()
    {
        _properties = new MaterialPropertyBlock();
        for (int i = 0; i < 5; i++)
        {
            string suffix = i == 0 ? "Master" : i.ToString();
            _levers[i] = transform.Find("Lever_" + suffix);
            _leds[i] = transform.Find("Led_" + suffix)?.GetComponent<Renderer>();
            _targets[i] = Quaternion.identity;
        }

        _text = GetComponentInChildren<TMP_Text>();
    }

    public void Apply(BreakerBoxRuntime.Snapshot snapshot)
    {
        bool master = (snapshot.Flags & 1) != 0;
        if (_flags != snapshot.Flags)
        {
            _flags = snapshot.Flags;
            for (int i = 0; i < 5; i++)
            {
                bool on = i == 0 ? master : master && (snapshot.Flags & (1 << i)) != 0;
                _targets[i] = Quaternion.Euler(on ? 0f : -75f, 0f, 0f);
            }

            _moving = true;
        }

        Paint(0, master ? 2 : 3);
        Paint(1, !master || (snapshot.Flags & 2) == 0 ? 0 : snapshot.A > 0 ? 2 : 1);
        Paint(2, !master || (snapshot.Flags & 4) == 0 ? 0 : snapshot.B > 0 ? 2 : 1);
        Paint(3, !master || (snapshot.Flags & 8) == 0 ? 0 : snapshot.C > 0 ? 2 : 1);
        Paint(4, !master || (snapshot.Flags & 16) == 0 ? 0 : snapshot.D > 0 ? 2 : 1);
        double signal = System.Math.Round(System.Math.Max(System.Math.Max(snapshot.A, snapshot.B), System.Math.Max(snapshot.C, snapshot.D)), 3);
        if (_text != null && (_display != snapshot.Display || _limit != snapshot.Limit || (snapshot.Display == 1 && _shownSignal != signal)))
        {
            _display = snapshot.Display;
            _limit = snapshot.Limit;
            _shownSignal = signal;
            _text.text = snapshot.Display == 0 ? snapshot.Limit == 0 ? "NO LIMIT" : "> " + BreakerState.Limits[snapshot.Limit] : signal.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    private void Paint(int index, int state)
    {
        if (_colors[index] == state || _leds[index] == null)
            return;
        _colors[index] = state;
        Color color = state == 0 ? Color.red : state == 1 ? Color.yellow : state == 2 ? Color.green : Color.black;
        _properties.Clear();
        _properties.SetColor("_BaseColor", color);
        _properties.SetColor("_Color", color);
        _leds[index].SetPropertyBlock(_properties);
    }

    private void Update()
    {
        if (!_moving)
            return;
        _moving = false;
        for (int i = 0; i < 5; i++)
        {
            if (_levers[i] == null)
                continue;
            _levers[i].localRotation = Quaternion.RotateTowards(_levers[i].localRotation, _targets[i], 360f * Time.deltaTime);
            _moving |= Quaternion.Angle(_levers[i].localRotation, _targets[i]) > 0.01f;
        }
    }
}
