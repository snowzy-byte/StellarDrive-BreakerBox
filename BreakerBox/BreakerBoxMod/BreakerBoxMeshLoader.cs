using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxMeshLoader
{
    private sealed class Group
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        public readonly Dictionary<string, int> Seen = new Dictionary<string, int>();
        public bool CompleteNormals = true;
    }

    private static Dictionary<string, Mesh> _meshes;
    private static Material _material;
    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
    public static Dictionary<string, Mesh> ParseObj(string text)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var groups = new Dictionary<string, Group>();
        string groupName = "Body";
        int lineNumber = 0;
        foreach (string raw in text.Split('\n'))
        {
            lineNumber++;
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (fields[0])
                {
                    case "v":
                        positions.Add(new Vector3(F(fields[1]), F(fields[2]), F(fields[3])));
                        break;
                    case "vn":
                        normals.Add(new Vector3(F(fields[1]), F(fields[2]), F(fields[3])).normalized);
                        break;
                    case "vt":
                        uvs.Add(new Vector2(F(fields[1]), F(fields[2])));
                        break;
                    case "o":
                    case "g":
                        groupName = fields.Length > 1 ? fields[1] : "Body";
                        break;
                    case "f":
                        if (fields.Length < 4)
                            throw new FormatException("Face has fewer than three vertices.");
                        if (!groups.TryGetValue(groupName, out Group group))
                            groups.Add(groupName, group = new Group());
                        var face = new List<int>();
                        for (int i = 1; i < fields.Length; i++)
                        {
                            string[] tuple = fields[i].Split('/');
                            int v = Index(tuple[0], positions.Count);
                            int t = tuple.Length > 1 && tuple[1].Length > 0 ? Index(tuple[1], uvs.Count) : -1;
                            int n = tuple.Length > 2 && tuple[2].Length > 0 ? Index(tuple[2], normals.Count) : -1;
                            string key = v + "/" + t + "/" + n;
                            if (!group.Seen.TryGetValue(key, out int index))
                            {
                                index = group.Vertices.Count;
                                group.Seen.Add(key, index);
                                group.Vertices.Add(positions[v]);
                                group.Normals.Add(n >= 0 ? normals[n] : Vector3.zero);
                                group.Uvs.Add(t >= 0 ? uvs[t] : Vector2.zero);
                                group.CompleteNormals &= n >= 0;
                            }

                            face.Add(index);
                        }

                        for (int i = 1; i + 1 < face.Count; i++)
                        {
                            group.Triangles.Add(face[0]);
                            group.Triangles.Add(face[i]);
                            group.Triangles.Add(face[i + 1]);
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                throw new FormatException("Invalid OBJ at line " + lineNumber + ": " + e.Message, e);
            }
        }

        var result = new Dictionary<string, Mesh>();
        foreach (var pair in groups)
        {
            Group data = pair.Value;
            if (data.Triangles.Count == 0)
                continue;
            var mesh = new Mesh
            {
                name = "BreakerBox_" + pair.Key,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (data.Vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            mesh.SetUVs(0, data.Uvs);
            mesh.SetColors(Enumerable.Repeat(Color.white, data.Vertices.Count).ToList());
            mesh.SetTriangles(data.Triangles, 0);
            if (data.CompleteNormals)
                mesh.SetNormals(data.Normals);
            else
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var tangents = new List<Vector4>();
            foreach (Vector3 normal in mesh.normals)
            {
                Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right, normal).normalized;
                tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
            }

            mesh.SetTangents(tangents);
            result.Add(pair.Key, mesh);
        }

        if (result.Count == 0)
            throw new FormatException("OBJ contains no faces.");
        return result;
    }

    private static float F(string value)
    {
        float result = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (float.IsNaN(result) || float.IsInfinity(result))
            throw new FormatException("Non-finite coordinate.");
        return result;
    }

    private static int Index(string value, int count)
    {
        int raw = int.Parse(value, CultureInfo.InvariantCulture);
        int index = raw < 0 ? count + raw : raw - 1;
        if (raw == 0 || index < 0 || index >= count)
            throw new FormatException("OBJ index out of range.");
        return index;
    }

    public static GameObject CreateMeshVisual(Material donor, string name, Color tint)
    {
        if (_meshes == null || _meshes.Values.Any(m => m == null))
        {
            using (Stream stream = typeof(BreakerBoxMeshLoader).Assembly.GetManifestResourceStream("BreakerBox.obj"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded resource BreakerBox.obj is missing.");
                using (var reader = new StreamReader(stream))
                    _meshes = ParseObj(reader.ReadToEnd());
            }
        }

        if (_material == null)
        {
            Shader shader = new[]
            {
                "Universal Render Pipeline/Lit"
            }.Select(Shader.Find).FirstOrDefault(s => s != null && s.isSupported);
            if (shader == null && donor == null)
                throw new InvalidOperationException("No supported material for BreakerBox");
            _material = shader != null ? new Material(shader) : new Material(donor);
            _material.name = "BreakerBox_Metal";
            _material.hideFlags = HideFlags.HideAndDontSave;
            foreach (string property in new[]
            {
                "_Albedo",
                "_BaseMap",
                "_MainTex"
            }

            )
                if (_material.HasProperty(property))
                    _material.SetTexture(property, Texture2D.whiteTexture);
            if (_material.HasProperty("_UseArray"))
                _material.SetFloat("_UseArray", 0f);
            if (_material.HasProperty("_Metallic"))
                _material.SetFloat("_Metallic", 0f);
            if (_material.HasProperty("_Smoothness"))
                _material.SetFloat("_Smoothness", 0.25f);
            _material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            Tint(_material, tint);
        }

        var root = new GameObject(name);
        foreach (var pair in _meshes)
        {
            if (pair.Key.StartsWith("Socket_", StringComparison.Ordinal))
                continue;
            var child = new GameObject(pair.Key);
            child.transform.SetParent(root.transform, false);
            Mesh mesh = pair.Value;
            if (pair.Key.StartsWith("Lever_", StringComparison.Ordinal))
            {
                bool master = pair.Key == "Lever_Master";
                int channel = master ? 0 : int.Parse(pair.Key.Substring(6));
                Vector3 size = master ? new Vector3(0.071f, 0.054f, 0.045f) : new Vector3(0.046f, 0.044f, 0.04f);
                Vector3 pivot = master ? new Vector3(-0.252f, -0.052f, 0.092f) : new Vector3(-0.068f + (channel - 1) * 0.112f, -0.0244f, 0.086f);
                mesh = HandleMesh(size);
                child.transform.localPosition = pivot;
                GameObject hinge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hinge.name = pair.Key + "_Hinge";
                hinge.transform.SetParent(root.transform, false);
                hinge.transform.localPosition = pivot;
                hinge.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                hinge.transform.localScale = new Vector3(0.018f, size.x * 0.5f, 0.018f);
                UnityEngine.Object.DestroyImmediate(hinge.GetComponent<Collider>());
                hinge.GetComponent<Renderer>().sharedMaterial = GroupMaterial("Breakers_Base");
            }

            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GroupMaterial(pair.Key);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        GameObject led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        led.name = "Led_Master";
        led.transform.SetParent(root.transform, false);
        led.transform.localPosition = new Vector3(-0.252f, 0.005f, 0.096f);
        led.transform.localScale = Vector3.one * 0.018f;
        UnityEngine.Object.DestroyImmediate(led.GetComponent<Collider>());
        led.GetComponent<Renderer>().sharedMaterial = GroupMaterial("Led_Master");
        var screen = new GameObject("DisplayText");
        screen.transform.SetParent(root.transform, false);
        screen.transform.localPosition = new Vector3(-0.252f, 0.085f, 0.084f);
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TextMeshPro text = screen.AddComponent<TextMeshPro>();
        text.font = TMP_Settings.defaultFontAsset;
        if (text.font == null)
            throw new InvalidOperationException("No TMP font for world-space display");
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(0.14f, 0.045f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.3f;
        text.fontSizeMax = 1.6f;
        text.color = Color.green;
        text.text = "NO LIMIT";
        var screenMaterial = new Material(text.fontSharedMaterial)
        {
            name = "BreakerBox_Display_Depth",
            hideFlags = HideFlags.HideAndDontSave
        };
        Shader depthShader = Shader.Find("TextMeshPro/Distance Field");
        if (depthShader != null && depthShader.isSupported)
            screenMaterial.shader = depthShader;
        screenMaterial.SetFloat("_ZTestMode", (float)CompareFunction.LessEqual);
        screenMaterial.SetFloat("_CullMode", (float)CullMode.Back);
        text.fontSharedMaterial = screenMaterial;
        root.AddComponent<BreakerBoxIndicators>();
        return root;
    }

    private static readonly Dictionary<Vector3, Mesh> Handles = new Dictionary<Vector3, Mesh>();
    private static Mesh HandleMesh(Vector3 size)
    {
        if (Handles.TryGetValue(size, out var mesh) && mesh != null)
            return mesh;
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh = UnityEngine.Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        UnityEngine.Object.DestroyImmediate(cube);
        mesh.name = "BreakerBox_Handle";
        mesh.hideFlags = HideFlags.HideAndDontSave;
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = Vector3.Scale(vertices[i], size) + Vector3.forward * size.z * 0.5f;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        Handles[size] = mesh;
        return mesh;
    }

    private static Material GroupMaterial(string group)
    {
        if (Materials.TryGetValue(group, out var material) && material != null)
            return material;
        Color color = new Color(0.65f, 0.70f, 0.76f);
        if (group == "Corner_Bolts")
            color = new Color(0.88f, 0.9f, 0.92f);
        else if (group == "Master_Housing" || group == "Breakers_Base")
            color = new Color(0.16f, 0.21f, 0.27f);
        else if (group == "Screen_Panel")
            color = new Color(0.025f, 0.07f, 0.06f);
        else if (group.StartsWith("Lever_"))
            color = group == "Lever_Master" ? new Color(0.95f, 0.32f, 0.09f) : new Color(0.86f, 0.88f, 0.91f);
        else if (group.StartsWith("Led_"))
            color = group == "Led_Master" ? Color.green : Color.yellow;
        material = new Material(_material)
        {
            name = "BreakerBox_" + group,
            hideFlags = HideFlags.HideAndDontSave
        };
        Tint(material, color);
        Materials[group] = material;
        return material;
    }

    private static void Tint(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
