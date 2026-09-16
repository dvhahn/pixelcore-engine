float4x4 MatrixTransform;

sampler2D SceneSampler : register(s0);

float2 SrcSize;
float Scale = 1.0;
float Sharpness = 1.0;

void VS(inout float4 pos : POSITION0, inout float4 color : COLOR0, inout float2 uv : TEXCOORD0)
{
    pos = mul(pos, MatrixTransform);
}

float4 PS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 pixPos = uv * SrcSize;
    float2 p = frac(pixPos);

    float k = Scale * Sharpness;
    float2 i = clamp(p * k, 0.0, 0.5) + clamp((p - 1.0) * k + 0.5, 0.0, 0.5);

    float2 uv2 = (floor(pixPos) + i) / SrcSize;
    return tex2D(SceneSampler, uv2) * color;
}

technique PixZoom
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
