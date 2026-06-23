// 程序化中性阴影面板：单个白像素 quad 拉伸到面板外扩矩形上，
// 由 SDF 圆角矩形生成柔和外阴影 / 半透明渐变填充 / 内缘细高光，
// 用于还原 GalGame 中那种悬浮、轻阴影、不张扬的中性 UI 质感
sampler2D TextureSampler : register(s0);

float2 PanelSize;     // 被绘制 quad 的尺寸（UI 像素，已含阴影外扩）
float Margin;         // 阴影外扩边距（像素），面板实体内缩这么多
float Radius;         // 圆角半径（像素）
float ShadowSoft;     // 阴影羽化宽度（像素）
float ShadowAlpha;    // 阴影最大不透明度
float2 ShadowOffset;  // 阴影相对面板的偏移（像素，正 y 向下）
float4 FillTop;       // 顶部填充色（rgb + 基础透明度）
float4 FillBottom;    // 底部填充色（rgb + 基础透明度）
float4 RimColor;      // 内缘高光色（rgb + 强度）
float RimWidth;       // 内缘高光宽度（像素）
float Alpha;          // 全局透明度（开合动画）

// 圆角矩形有符号距离场：<0 在内部
float sdRoundBox(float2 p, float2 halfSize, float r)
{
    float2 q = abs(p) - halfSize + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// 直 alpha 的 "over" 合成（src 叠在 dst 之上）
float4 OverStraight(float4 dst, float4 src)
{
    float outA = src.a + dst.a * (1.0 - src.a);
    float3 outRGB = outA <= 0.0001 ? float3(0, 0, 0)
        : (src.rgb * src.a + dst.rgb * dst.a * (1.0 - src.a)) / outA;
    return float4(outRGB, outA);
}

float4 MainPS(float2 texCoord : TEXCOORD0) : COLOR
{
    float2 px = texCoord * PanelSize;
    float2 center = PanelSize * 0.5;
    float2 half = center - Margin;           // 面板实体半尺寸
    float r = min(Radius, min(half.x, half.y));

    float2 p = px - center;
    float sd = sdRoundBox(p, half, r);

    // 1) 柔和外阴影（向下偏移、羽化）
    float sdShadow = sdRoundBox(p - ShadowOffset, half, r);
    float shadowMask = (1.0 - smoothstep(-ShadowSoft, ShadowSoft, sdShadow)) * ShadowAlpha;
    float4 result = float4(0.0, 0.0, 0.0, shadowMask);

    // 2) 半透明填充 + 自上而下的轻微渐变
    float4 fillCol = lerp(FillTop, FillBottom, saturate(texCoord.y));
    float fillCov = (1.0 - smoothstep(-1.0, 1.0, sd)) * fillCol.a;
    result = OverStraight(result, float4(fillCol.rgb, fillCov));

    // 3) 内缘细高光：仅面板内侧靠近边缘处
    float inside = step(sd, 0.0);
    float rim = (1.0 - smoothstep(0.0, RimWidth, abs(sd))) * inside;
    result = OverStraight(result, float4(RimColor.rgb, rim * RimColor.a));

    result.a *= Alpha;
    return result;
}

technique Technique1
{
    pass SoftPanelPass
    {
        PixelShader = compile ps_3_0 MainPS();
    }
}
