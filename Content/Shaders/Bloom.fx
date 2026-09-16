float4x4 MatrixTransform;

sampler2D SceneSampler : register(s0);

float Threshold = 0.9;
float Knee = 0.45;

void VS(inout float4 pos : POSITION0, inout float4 color : COLOR0, inout float2 uv : TEXCOORD0)
{
    pos = mul(pos, MatrixTransform);
}

float4 PS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(SceneSampler, uv);
    float brightness = max(c.r, max(c.g, c.b));
    float soft = clamp(brightness - Threshold + Knee, 0.0, 2.0 * Knee);
    soft = soft * soft / (4.0 * Knee + 1e-4);
    float mult = max(soft, brightness - Threshold) / max(brightness, 1e-4);
    return float4(c.rgb * mult, 1.0);
}

technique Prefilter
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
