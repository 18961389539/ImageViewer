// 伪彩色着色器。
// Chinese: 调色板不再在这里定义——hot / jet / viridis 的取值只有一份，在 C# 侧的 256 项 LUT
// （ImageViewerDisplaySourceService.CreatePaletteLut）里；本着色器只做两件事：
//   ① 用与 CPU 路径相同的权重把输入换算成强度；
//   ② 按 texel 中心采样那张 LUT。
// 这样 CPU 回退路径与 Shader 路径的结果在构造上一致，不会各自漂移。
// English: The palette is defined once, in the C# LUT; this shader only converts the input to an intensity with the same
// weights as the CPU path and samples that LUT at texel centers, so both paths agree by construction.
sampler2D Input : register(s0);
sampler2D PaletteLut : register(s1);
float PaletteIndex : register(c0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 inputColor = tex2D(Input, uv);

    if (PaletteIndex < 0.5)
    {
        return inputColor;
    }

    // 与 ImageViewerPixelAccess.ReadIntensity 同一组权重。
    float intensity = saturate(dot(inputColor.rgb, float3(0.299, 0.587, 0.114)));

    // 采样 texel 中心（LUT 宽 256）：落在与 CPU 整数索引同一格上，避免线性过滤在相邻色阶之间插值。
    float lutU = (intensity * 255.0 + 0.5) / 256.0;
    float3 color = tex2D(PaletteLut, float2(lutU, 0.5)).rgb;

    // 保留输入 alpha：透明 PNG 在 CPU 与 GPU 路径上都保持透明。
    return float4(color, inputColor.a);
}
