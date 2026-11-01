using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxSurface
{
    public static readonly Vector2 AtlasMin = new Vector2(0.035f, 0.365f);
    public static readonly Vector2 AtlasMax = new Vector2(0.229f, 0.438f);
    public static readonly Vector2 WhitePalette = new Vector2(0.11365709f, 0.00599778f);
    public static readonly Vector2 RedPalette = new Vector2(.140625f, .015625f);
    public static readonly Vector2 DarkGrayPalette = new Vector2(.078125f, .015625f);

    public static Vector2 Palette(string name) => name == "Lever_Master" ? RedPalette : name == "Panels" ? DarkGrayPalette : WhitePalette;

    public static Vector2 Atlas(float u, float v) => new Vector2(
        AtlasMin.x + u * (AtlasMax.x - AtlasMin.x), AtlasMin.y + v * (AtlasMax.y - AtlasMin.y));
}
