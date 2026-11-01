using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BreakerBoxMod;
public static class BreakerBoxModel
{
    private static readonly Dictionary<(Color, bool), Material> Materials = new Dictionary<(Color, bool), Material>();
    private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
    private static List<BreakerBoxGeometry.Piece> _geometry;
    private static Material _displayMaterial;

    public static GameObject CreateMeshVisual(Material donor, string name)
    {
        if (_geometry == null)
            _geometry = BreakerBoxGeometry.Create();
        var root = new GameObject(name);
        foreach (var piece in _geometry)
        {
            var child = new GameObject(piece.Name);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = piece.Position;
            child.AddComponent<MeshFilter>().sharedMesh = MeshFor(piece);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(piece.Color, piece.Painted, donor);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        var screen = new GameObject("DisplayText");
        screen.transform.SetParent(root.transform, false);
        screen.transform.localPosition = BreakerBoxLayout.DisplayPosition;
        screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var text = screen.AddComponent<TextMeshPro>();
        text.font = TMP_Settings.defaultFontAsset;
        if (text.font == null)
            throw new InvalidOperationException("No TMP font for world-space display");
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(.137f, .048f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.3f;
        text.fontSizeMax = 1.6f;
        text.color = Color.green;
        text.text = "NO LIMIT";
        if (_displayMaterial == null)
        {
            _displayMaterial = new Material(text.fontSharedMaterial)
            {
                name = "BreakerBox_Display_Depth",
                hideFlags = HideFlags.HideAndDontSave
            };
            Shader depthShader = Shader.Find("TextMeshPro/Distance Field");
            if (depthShader != null && depthShader.isSupported)
                _displayMaterial.shader = depthShader;
            _displayMaterial.SetFloat("_ZTestMode", (float)CompareFunction.LessEqual);
            _displayMaterial.SetFloat("_CullMode", (float)CullMode.Back);
        }
        text.fontSharedMaterial = _displayMaterial;
        root.AddComponent<BreakerBoxIndicators>();
        return root;
    }

    private static Mesh MeshFor(BreakerBoxGeometry.Piece piece)
    {
        if (Meshes.TryGetValue(piece.Name, out Mesh mesh) && mesh != null)
            return mesh;
        mesh = new Mesh { name = "BreakerBox_" + piece.Name, hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(piece.Vertices);
        mesh.SetTriangles(piece.Triangles, 0);
        mesh.SetUVs(0, piece.Uvs);
        mesh.SetUVs(1, Enumerable.Repeat(BreakerBoxSurface.Palette(piece.Name), piece.Vertices.Count).ToList());
        mesh.SetColors(Enumerable.Repeat(Color.white, piece.Vertices.Count).ToList());
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var tangents = new List<Vector4>();
        foreach (Vector3 normal in mesh.normals)
        {
            Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right, normal).normalized;
            tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
        }
        mesh.SetTangents(tangents);
        Meshes[piece.Name] = mesh;
        return mesh;
    }

    private static Material MaterialFor(Color color, bool painted, Material donor)
    {
        var key = (color, painted);
        if (Materials.TryGetValue(key, out Material material) && material != null)
            return material;
        bool native = painted && donor != null && donor.HasProperty("_ColorPalette") && donor.GetTexture("_ColorPalette") != null;
        Shader shader = native ? donor.shader : Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || !shader.isSupported) shader = donor != null ? donor.shader : null;
        if (shader == null)
            throw new InvalidOperationException("No supported material for BreakerBox");
        material = native ? new Material(donor) : new Material(shader);
        material.name = "BreakerBox_Paint";
        material.hideFlags = HideFlags.HideAndDontSave;
        if (!native)
            foreach (string property in new[] { "_Albedo", "_BaseMap", "_MainTex" })
                if (material.HasProperty(property))
                    material.SetTexture(property, Texture2D.whiteTexture);
        if (material.HasProperty("_UseArray")) material.SetFloat("_UseArray", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.25f);
        Color tint = native ? Color.white : color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        Materials[key] = material;
        return material;
    }
}
