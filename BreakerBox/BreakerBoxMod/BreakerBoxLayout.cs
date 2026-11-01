using UnityEngine;

namespace BreakerBoxMod;
public static class BreakerBoxLayout
{
    public const float SocketDepth = 0.07f;
    public const float CenterY = -0.125f;
    public const float ChannelPitch = 0.25f;
    public const float MasterX = -.610f;
    public const float MasterFrameWidth = .172f;
    public const float MasterFrameFront = .134f;
    public const float BodyLeft = -0.71875f;
    public const float BodyRight = 0.5f;
    public const float BodyHalfHeight = 0.21875f;
    public const float BodyDepth = .14f;
    public static readonly Vector3 Size = new Vector3(1.25f, 0.5f, 0.20f);
    public static readonly Vector3 BoundsCenter = new Vector3(0.109375f, CenterY, -BodyDepth / 2f);
    public static readonly Vector3 BoundsSize = new Vector3(1.21875f, 0.4375f, BodyDepth);
    public static readonly Vector3 TripPosition = new Vector3(-0.75f, 0f, SocketDepth);
    public static readonly Vector3 DisplayPosition = new Vector3(MasterX, 0.10f, .121f);

    public static float ChannelX(int channel) => 0.375f - channel * ChannelPitch;
    public static Vector3 Input(int channel) => new Vector3(ChannelX(channel), CenterY - 0.25f, SocketDepth);
    public static Vector3 Output(int channel) => new Vector3(ChannelX(channel), CenterY + 0.25f, SocketDepth);
    public static Vector3 LeverPivot(int channel) => channel < 0
        ? new Vector3(MasterX, -0.105f, .119f)
        : new Vector3(ChannelX(channel), -0.055f, .119f);
}
