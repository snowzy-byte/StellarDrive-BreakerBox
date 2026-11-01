using System;
using System.Collections.Generic;
using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxGeometry
{
    public sealed class Piece
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly Color Color;
        public readonly bool Painted;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public Piece(string name, Color color, Vector3 position = default, bool painted = false)
        {
            Name = name;
            Color = color;
            Position = position;
            Painted = painted;
        }

        public void Face(params Vector3[] points)
        {
            int start = Vertices.Count;
            Vertices.AddRange(points);
            Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]);
            var projected = new List<Vector2>();
            foreach (Vector3 point in points)
            {
                if (Math.Abs(normal.z) >= Math.Abs(normal.x) && Math.Abs(normal.z) >= Math.Abs(normal.y))
                    projected.Add(new Vector2(point.x, point.y));
                else if (Math.Abs(normal.x) >= Math.Abs(normal.y))
                    projected.Add(new Vector2(point.z, point.y));
                else
                    projected.Add(new Vector2(point.x, point.z));
            }
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (Vector2 p in projected)
            {
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            foreach (Vector2 p in projected)
                Uvs.Add(BreakerBoxSurface.Atlas((p.x-minX) / Math.Max(maxX-minX, .000001f),
                    (p.y-minY) / Math.Max(maxY-minY, .000001f)));
            for (int i = 1; i < points.Length - 1; i++)
                Triangles.AddRange(new[] { start, start + i, start + i + 1 });
        }

        public void Extrude(Vector2[] outline, float back, float front)
        {
            var a = new Vector3[outline.Length];
            var b = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                a[i] = new Vector3(outline[i].x, outline[i].y, front);
                b[outline.Length - 1 - i] = new Vector3(outline[i].x, outline[i].y, back);
                Vector2 next = outline[(i + 1) % outline.Length];
                Face(new Vector3(outline[i].x, outline[i].y, back),
                    new Vector3(next.x, next.y, back), new Vector3(next.x, next.y, front), a[i]);
            }
            Face(a);
            Face(b);
        }

        public void Box(float x, float y, float width, float height, float back, float front, float bevel = 0f)
        {
            float l = x - width / 2f, r = x + width / 2f;
            float d = y - height / 2f, u = y + height / 2f;
            if (bevel <= 0f)
                Extrude(new[] { new Vector2(l, d), new Vector2(r, d), new Vector2(r, u), new Vector2(l, u) }, back, front);
            else
                Extrude(new[] { new Vector2(l + bevel, d), new Vector2(r - bevel, d),
                    new Vector2(r, d + bevel), new Vector2(r, u - bevel), new Vector2(r - bevel, u),
                    new Vector2(l + bevel, u), new Vector2(l, u - bevel), new Vector2(l, d + bevel) }, back, front);
        }

        public void Disc(float x, float y, float radius, float back, float front, int sides = 8)
        {
            var outline = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                double angle = i * 2d * Math.PI / sides;
                outline[i] = new Vector2(x + radius * (float)Math.Cos(angle), y + radius * (float)Math.Sin(angle));
            }
            Extrude(outline, back, front);
        }

        public void BeveledBox(float x, float width, float height, float back, float front)
        {
            Box(x, 0f, width, height, back, front - 0.006f, 0.018f);
            float l = x - width / 2f, r = x + width / 2f, h = height / 2f, c = 0.018f;
            Vector2[] outline = { new Vector2(l+c,-h), new Vector2(r-c,-h), new Vector2(r,-h+c),
                new Vector2(r,h-c), new Vector2(r-c,h), new Vector2(l+c,h), new Vector2(l,h-c), new Vector2(l,-h+c) };
            var face = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % outline.Length];
                Vector3 a = new Vector3(p.x, p.y, front - .006f), b = new Vector3(q.x, q.y, front - .006f);
                Vector3 insetP = new Vector3(x + (p.x-x) * (width-.012f)/width, p.y*(height-.012f)/height, front);
                Vector3 insetQ = new Vector3(x + (q.x-x) * (width-.012f)/width, q.y*(height-.012f)/height, front);
                Face(a, b, insetQ, insetP);
                face[i] = insetP;
            }
            Face(face);
        }

        public void RecessFrame(float x, float y, float width, float height, float border,
            float back, float front, float floor)
        {
            Vector3[] outer = Outline(x, y, width, height, .012f, back);
            Vector3[] lip = Outline(x, y, width - .004f, height - .004f, .010f, front);
            Vector3[] opening = Outline(x, y, width - border * 2f, height - border * 2f, .006f, front);
            Vector3[] inset = Outline(x, y, width - border * 2f - .008f, height - border * 2f - .008f, .004f, floor);
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                Face(outer[i], outer[next], lip[next], lip[i]);
                Face(lip[i], lip[next], opening[next], opening[i]);
                Face(opening[i], opening[next], inset[next], inset[i]);
                Face(outer[next], outer[i], inset[i], inset[next]);
            }
        }

        private static Vector3[] Outline(float x, float y, float width, float height, float corner, float z)
        {
            float l = x-width/2f, r = x+width/2f, b = y-height/2f, t = y+height/2f;
            return new[] { new Vector3(l+corner,b,z), new Vector3(r-corner,b,z),
                new Vector3(r,b+corner,z), new Vector3(r,t-corner,z), new Vector3(r-corner,t,z),
                new Vector3(l+corner,t,z), new Vector3(l,t-corner,z), new Vector3(l,b+corner,z) };
        }
    }

    public static List<Piece> Create()
    {
        var body = new Piece("Body", new Color(0.22f, 0.40f, 0.52f), painted: true);
        var frame = new Piece("Front_Frame", new Color(0.80f, 0.83f, 0.82f), painted: true);
        float center = (BreakerBoxLayout.BodyLeft + BreakerBoxLayout.BodyRight) / 2f;
        float caseWidth = BreakerBoxLayout.BodyRight - BreakerBoxLayout.BodyLeft;
        body.BeveledBox(center, caseWidth, BreakerBoxLayout.BodyHalfHeight * 2f, 0f, .10f);
        frame.RecessFrame(center, 0f, caseWidth, BreakerBoxLayout.BodyHalfHeight * 2f, .018f, .10f, .14f, .107f);
        frame.Box(-.513f, 0f, .022f, .398f, .10f, BreakerBoxLayout.MasterFrameFront, .005f);
        var panels = new Piece("Panels", new Color(.16f, .19f, .22f), painted: true);
        var slots = new Piece("Switch_Slots", new Color(.045f, .065f, .075f));
        var labels = new Piece("Channel_Numbers", new Color(0.84f, 0.88f, 0.9f), painted: true);
        var bolts = new Piece("Bolts", new Color(0.78f, 0.84f, 0.85f));
        var screen = new Piece("Screen_Panel", new Color(0.018f, 0.035f, 0.03f),
            new Vector3(BreakerBoxLayout.MasterX, 0.10f, 0f));
        screen.Box(0f, 0f, 0.144f, 0.055f, .11f, .116f);
        var result = new List<Piece> { body, frame, panels, slots, labels, bolts, screen };
        frame.RecessFrame(BreakerBoxLayout.MasterX, .10f, BreakerBoxLayout.MasterFrameWidth, .087f, .010f,
            .10f, BreakerBoxLayout.MasterFrameFront, .116f);
        panels.Box(BreakerBoxLayout.MasterX, -.075f, .15f, .219f, .101f, .113f, .004f);
        frame.RecessFrame(BreakerBoxLayout.MasterX, -.075f, BreakerBoxLayout.MasterFrameWidth, .235f, .008f,
            .10f, BreakerBoxLayout.MasterFrameFront, .114f);
        for (int i = -1; i < 4; i++)
        {
            bool master = i < 0;
            float x = master ? BreakerBoxLayout.MasterX : BreakerBoxLayout.ChannelX(i);
            if (!master)
            {
                panels.Box(x, 0f, .20f, .344f, .101f, .113f, .006f);
                frame.RecessFrame(x, 0f, .216f, .360f, .008f, .10f, .128f, .114f);
                Number(labels, i + 1, x, 0.12f);
            }
            string suffix = master ? "Master" : (i + 1).ToString();
            Vector3 pivot = BreakerBoxLayout.LeverPivot(i);
            float width = master ? 0.09f : 0.08f;
            slots.Box(x, master ? -.093f : -.041f, width + .025f, master ? .145f : .14f, .113f, .114f, .004f);
            panels.Box(x, pivot.y, width + 0.02f, 0.022f, .114f, .124f, 0.003f);
            var lever = new Piece("Lever_" + suffix, master ? new Color(.82f, .065f, .05f) : labels.Color, pivot, true);
            lever.Box(0f, 0f, width, master ? 0.07f : 0.065f, 0f, master ? 0.065f : 0.06f, 0.004f);
            var led = new Piece("Led_" + suffix, Color.white, new Vector3(x, master ? -0.015f : 0.055f, 0f));
            slots.Disc(x, led.Position.y, 0.02f, .113f, .117f);
            led.Disc(0f, 0f, 0.014f, .117f, .123f);
            result.Add(lever);
            result.Add(led);
        }
        foreach (float x in new[] { BreakerBoxLayout.BodyLeft + 0.02f, BreakerBoxLayout.BodyRight - 0.02f })
            foreach (float y in new[] { -0.203f, 0.203f })
                bolts.Disc(x, y, 0.007f, .14f, .145f, 6);
        return result;
    }

    private static void Number(Piece mesh, int number, float x, float y)
    {
        int[] masks = { 0, 0x06, 0x5b, 0x4f, 0x66 };
        float[,] segments = { { 0f, .023f, .024f, .004f }, { .012f, .012f, .004f, .022f },
            { .012f, -.012f, .004f, .022f }, { 0f, -.023f, .024f, .004f },
            { -.012f, -.012f, .004f, .022f }, { -.012f, .012f, .004f, .022f }, { 0f, 0f, .024f, .004f } };
        for (int i = 0; i < 7; i++)
            if ((masks[number] & (1 << i)) != 0)
            {
                float cx = x - segments[i, 0], cy = y + segments[i, 1];
                float w = segments[i, 2] / 2f, h = segments[i, 3] / 2f;
                mesh.Face(new Vector3(cx - w, cy - h, .114f), new Vector3(cx + w, cy - h, .114f),
                    new Vector3(cx + w, cy + h, .114f), new Vector3(cx - w, cy + h, .114f));
            }
    }
}
