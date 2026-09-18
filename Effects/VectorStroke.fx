// ============================================================================
// VectorStroke.fx  矢量描边 SDF 抗锯齿（InnoVault.Vectors 网格后端自带）
// vs_2_0 / ps_3_0，vs+ps 成对；全直线、零流程控制（无 if / return / 动态循环）
// 顶点布局 = VertexPositionColorTexture：TexCoord.y 横跨条带 0→1，中线 0.5
// 参数：transformMatrix 顶点变换；uAA 软边像素；uGlow 中心提亮；uGlowPower 提亮衰减幂；uTextured 0/1 是否乘 s0 贴图
// ============================================================================

matrix transformMatrix;
float uAA = 1.0;
float uGlow = 0.0;
float uGlowPower = 2.0;
float uTextured = 0.0;

// 批次主贴图位，由 VectorRenderer 在 Apply 之后绑定
sampler2D texSampler : register(s0);

struct VSInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

VSOutput VS(VSInput input)
{
    VSOutput output;
    output.Position = mul(input.Position, transformMatrix);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PS(VSOutput input) : COLOR0
{
    // 到中线的归一距离：中线 0，两边缘 1
    float d = abs(input.TexCoord.y - 0.5) * 2.0;
    // d 每像素的变化量 × 软边宽度 = 过渡带（以 d 计）
    float aa = max(fwidth(input.TexCoord.y) * 2.0 * uAA, 0.0001);
    float cov = 1.0 - smoothstep(1.0 - aa, 1.0, d);
    // 中心提亮：中线最亮，按幂衰减到边缘
    float glow = pow(saturate(1.0 - d), uGlowPower) * uGlow;
    // 贴图项恒定采样，uTextured 为 0 时被 lerp 抹平
    float4 tex = tex2D(texSampler, input.TexCoord);
    float4 col = input.Color * lerp(float4(1.0, 1.0, 1.0, 1.0), tex, uTextured);
    // 预乘语义：alpha 只吃覆盖率，提亮只作用于 rgb
    return float4(col.rgb * cov * (1.0 + glow), col.a * cov);
}

technique Technique1
{
    pass VectorStrokePass
    {
        VertexShader = compile vs_2_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
