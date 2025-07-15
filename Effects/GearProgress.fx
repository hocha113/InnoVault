sampler2D TextureSampler : register(s0);
float Progress;  // [0.0 ~ 1.0]
float Rotation;  // 当前图片的旋转角度（弧度）

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR
{
    float2 center = float2(0.5, 0.5);
    float2 offset = texCoord - center;

    //先旋转回未旋转状态（通过逆旋转）
    float cosR = cos(-Rotation);
    float sinR = sin(-Rotation);
    float2 unrotatedOffset = float2(
        offset.x * cosR - offset.y * sinR,
        offset.x * sinR + offset.y * cosR
    );

    float angle = atan2(unrotatedOffset.y, unrotatedOffset.x); // [-PI, PI]
    if (angle < 0)
        angle += 6.2831853; // [0, 2π]

    float maxAngle = Progress * 6.2831853;
    float isShadow = angle > maxAngle ? 1.0 : 0.0;

    float shadowStrength = 0.2;//阴影强度
    float brightness = lerp(1.0, shadowStrength, isShadow);

    float4 color = tex2D(TextureSampler, texCoord);
    return float4(color.rgb * brightness, color.a);
}

technique Technique1
{
    pass GearProgressPass
    {
        PixelShader = compile ps_2_0 MainPS();
    }
}