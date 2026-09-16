float4x4 MatrixTransform;

sampler2D SceneSampler : register(s0);

texture2D LutTexture;
sampler2D LutSampler = sampler_state
{
    Texture = <LutTexture>;
    MinFilter = Linear; MagFilter = Linear;
    AddressU = Clamp; AddressV = Clamp;
};

texture2D LutTextureB;
sampler2D LutSamplerB = sampler_state
{
    Texture = <LutTextureB>;
    MinFilter = Linear; MagFilter = Linear;
    AddressU = Clamp; AddressV = Clamp;
};

texture2D BloomTexture;
sampler2D BloomSampler = sampler_state
{
    Texture = <BloomTexture>;
    MinFilter = Linear; MagFilter = Linear;
    AddressU = Clamp; AddressV = Clamp;
};

float2 DestOrigin;
float2 DestSize;

float LutSize = 32.0;
float LutAmount = 0.0;
float LutBlend = 0.0;

float4 VignetteColor;
float4 VignetteParams;

float4 FogColor;

float4 GrainParams;
float GrainSeed;

float ChromAb = 0.0;
float Distortion = 0.0;

float2 DestPixels;

float BloomIntensity = 0.0;
float4 BloomTint;

void VS(inout float4 pos : POSITION0, inout float4 color : COLOR0, inout float2 uv : TEXCOORD0)
{
    pos = mul(pos, MatrixTransform);
}

float3 SampleLut(sampler2D lut, float3 c)
{
    float sliceSize = 1.0 / LutSize;
    float slicePixel = sliceSize / LutSize;
    float sliceInner = slicePixel * (LutSize - 1.0);
    float z = c.b * (LutSize - 1.0);
    float z0 = floor(z);
    float z1 = min(z0 + 1.0, LutSize - 1.0);
    float x = slicePixel * 0.5 + c.r * sliceInner;
    float y = 0.5 / LutSize + c.g * (LutSize - 1.0) / LutSize;
    float3 a = tex2D(lut, float2(x + z0 * sliceSize, y)).rgb;
    float3 b = tex2D(lut, float2(x + z1 * sliceSize, y)).rgb;
    return lerp(a, b, z - z0);
}

float Hash(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float3 ColorDodge(float3 base, float blend)
{
    return min(base / max(1.0 - blend, 1e-4), 1.0);
}

float Bayer2(float2 a)
{
    a = floor(a);
    return frac(a.x * 0.5 + a.y * a.y * 0.75);
}
#define Bayer4(a) (Bayer2(0.5 * (a)) * 0.25 + Bayer2(a))
#define Bayer8(a) (Bayer4(0.5 * (a)) * 0.25 + Bayer2(a))

float4 PS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 duv = (uv - DestOrigin) / DestSize;
    float2 cc = duv - 0.5;

    float r2 = dot(cc, cc);
    duv = 0.5 + cc * (1.0 - Distortion * 0.3 * r2);
    float2 suv = DestOrigin + duv * DestSize;

    float2 caOff = (duv - 0.5) * ChromAb * 0.03 * DestSize;
    float4 src = tex2D(SceneSampler, suv);
    float3 col;
    col.r = tex2D(SceneSampler, suv - caOff).r;
    col.g = src.g;
    col.b = tex2D(SceneSampler, suv + caOff).b;

    col += tex2D(BloomSampler, suv).rgb * BloomIntensity * BloomTint.rgb;

    col = lerp(col, FogColor.rgb, FogColor.a);

    float2 vd = abs(duv - 0.5) * VignetteParams.x;
    vd.x *= VignetteParams.z;
    float vig = pow(saturate(1.0 - dot(vd, vd)), VignetteParams.y);
    col *= lerp(VignetteColor.rgb, float3(1.0, 1.0, 1.0), lerp(1.0, vig, VignetteParams.w));

    float3 graded = saturate(col);
    col = lerp(col, lerp(SampleLut(LutSampler, graded), SampleLut(LutSamplerB, graded), LutBlend), LutAmount);

    float2 cell = floor(duv * GrainParams.zw) / GrainParams.zw;
    float n = Hash(cell + frac(GrainSeed * 0.1));
    col = ColorDodge(col, n * GrainParams.x * 0.03);

    col += (Bayer8(duv * DestPixels) - 0.5) * (1.0 / 255.0);

    return float4(col, src.a) * color;
}

technique PostCompose
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
