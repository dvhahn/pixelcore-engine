float4x4 MatrixTransform;

sampler2D NoiseSampler : register(s0);

float2 RtSize;
float  ArtScale;
float2 Origin;

float4 SkyTop;
float4 SkyBottom;
float  SkyBands;
float  Dither;

float4 CloudLight;
float4 CloudBody;
float4 CloudShadow;

float2 SunDir;
float  Relief;
float  Stretch;
float  Smooth;
float  Shade;
float  ShadowDepth;
float  FlatBase;
float  RowHeight;
float  Lumpy;
float  Shape;
float  Fluff;
float  Coverage;
float  Detail;
float  Haze;
float  LayerCount;

float4 LayerA;
float4 LayerB;
float4 LayerC;

void VS(inout float4 pos : POSITION0, inout float4 color : COLOR0, inout float2 uv : TEXCOORD0)
{
    pos = mul(pos, MatrixTransform);
}

float Density(float2 p, float4 L)
{
    float2 q = p + L.yz;
    q.x /= Stretch;
    float2 uvA = q / L.x;
    float2 uvB = (q * 0.61 + float2(L.w, 0.0)) / L.x;
    float4 ta = tex2D(NoiseSampler, uvA);
    float4 tb = tex2D(NoiseSampler, uvB);
    float a = lerp(ta.r, ta.b, Smooth);
    float b = lerp(tb.g, tb.a, Smooth);
    float n = lerp(lerp(a, b, Detail), max(a, b), Lumpy);

    float rowH = RowHeight * (L.x / LayerA.x);
    float row = frac(q.y / rowH);
    float bump = smoothstep(0.30, 0.62, row) * (1.0 - smoothstep(0.72, 1.0, row));
    return n - FlatBase * 0.5 * bump;
}

float3 CloudLayer(float2 p, float4 L, float th, float haze, float3 sky, out float mask)
{
    float n = Density(p, L);
    mask = step(th, n);

    float dep = max(ShadowDepth, 1.0);
    float under = step(Density(p - SunDir, L), th);
    under = max(under, step(Density(p - SunDir * dep * 0.5, L), th));
    under = max(under, step(Density(p - SunDir * dep, L), th));
    float under2 = step(Density(p - SunDir * dep * 2.0, L), th);
    under2 = max(under2, step(Density(p - SunDir * dep * 1.5, L), th));
    float3 mid = lerp(CloudBody.rgb, CloudShadow.rgb, 0.5);
    float3 two = lerp(lerp(CloudBody.rgb, mid, under2), CloudShadow.rgb, under);

    float nSun = Density(p + SunDir, L);
    float d = max(2.0, L.x * 0.06);
    float slope = Density(p + SunDir * d, L) - n;
    float3 three = CloudBody.rgb;
    three = (slope >  Relief) ? CloudShadow.rgb : three;
    three = (slope < -Relief) ? CloudLight.rgb  : three;
    three = (under > 0.5) ? CloudShadow.rgb : three;
    three = (nSun  < th) ? CloudLight.rgb  : three;

    float3 c = (Shade < 0.5) ? CloudBody.rgb : ((Shade < 1.5) ? two : three);
    return lerp(c, sky, haze);
}

float Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float3 PuffLayer(float2 p, float4 L, float haze, float3 sky, out float mask)
{
    float2 q = p + L.yz;
    float cellW = L.x;
    float cellH = L.x * 0.55;
    float2 cell = floor(q / float2(cellW, cellH));

    float sdf = 1e5;
    float2 nrm = float2(0.0, -1.0);
    for (int cx = -1; cx <= 1; cx++)
    {
        float2 c = cell + float2(cx, 0.0);
        float h0 = Hash21(c * 1.7 + 3.1);
        float has = step(h0, Coverage * 1.4);
        float h1 = Hash21(c + 5.1), h2 = Hash21(c + 9.7), h3 = Hash21(c + 13.3);
        float W = cellW * (0.28 + 0.55 * h1 * h1);
        float R = W * (0.17 + 0.06 * h3);
        float2 center = (c + 0.5) * float2(cellW, cellH)
                      + float2((h2 - 0.5) * (cellW - W), (h3 - 0.5) * max(cellH - 2.4 * R, 0.0));
        float baseY = center.y + R * 0.5;

        float lobes = 4.0 + floor(h2 * 3.0);
        for (int i = 0; i < 6; i++)
        {
            float t = (float(i) - (lobes - 1.0) * 0.5) / max((lobes - 1.0) * 0.5, 1.0);
            float hi = Hash21(c + float2(float(i) * 3.7, 1.9));
            float on = step(float(i), lobes - 0.5);
            float r = R * (0.55 + 0.45 * (1.0 - t * t)) * (0.85 + 0.3 * hi);
            float2 lc = float2(center.x + t * W * 0.42, baseY - r * 0.75 - (hi - 0.5) * R * 0.3);
            float d = length(q - lc) - r + (1.0 - has * on) * 1e5;
            float take = step(d, sdf);
            nrm = lerp(nrm, (q - lc) / r, take);
            sdf = min(sdf, d);
        }
        float tall = step(0.45, h1);
        for (int j = 0; j < 3; j++)
        {
            float t2 = (float(j) - 1.0) * 0.5;
            float hj = Hash21(c + float2(float(j) * 5.3, 8.1));
            float r2 = R * (0.62 + 0.28 * (1.0 - t2 * t2 * 4.0)) * (0.85 + 0.3 * hj);
            float2 lc2 = float2(center.x + t2 * W * 0.42 * 0.9, baseY - R * 0.75 - r2 * 0.95);
            float d2 = length(q - lc2) - r2 + (1.0 - has * tall) * 1e5;
            float take2 = step(d2, sdf);
            nrm = lerp(nrm, (q - lc2) / r2, take2);
            sdf = min(sdf, d2);
        }
        float2 sc = float2(center.x, baseY - R * 0.45);
        float2 sd = (q - sc) / float2(W * 0.46, R * 0.55);
        float ds = (length(sd) - 1.0) * R * 0.55 + (1.0 - has) * 1e5;
        float takeS = step(ds, sdf);
        nrm = lerp(nrm, sd, takeS);
        sdf = min(sdf, ds);
    }

    float fl = tex2D(NoiseSampler, (q * 1.7) / L.x).r - 0.5;
    sdf += fl * Fluff;

    mask = step(sdf, 0.0);

    float2 ldir = normalize(SunDir + float2(-0.3, 0.0));
    float lit = dot(nrm, ldir);
    float3 mid = lerp(CloudBody.rgb, CloudShadow.rgb, 0.5);
    float3 c3 = CloudBody.rgb;
    c3 = (lit < -0.38) ? mid : c3;
    c3 = (lit < -0.68) ? CloudShadow.rgb : c3;
    c3 = (lit >  0.58) ? CloudLight.rgb : c3;
    return lerp(c3, sky, haze);
}

float4 PS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float2 art = RtSize / ArtScale;
    float2 pix = floor(uv * art);
    float2 p   = pix + Origin;

    float2 m = fmod(pix, 2.0);
    float bayer = ((m.x * 2.0 + m.y * 3.0 - m.x * m.y * 4.0) / 4.0 - 0.375) * 0.5;
    float t = (pix.y + 0.5) / art.y;
    float bands = max(SkyBands, 2.0);
    float tq = t + Dither * bayer / bands;
    float band = clamp(floor(saturate(tq) * bands), 0.0, bands - 1.0) / max(bands - 1.0, 1.0);
    float tt = (SkyBands < 0.5) ? saturate(t) : band;
    float3 sky = lerp(SkyTop.rgb, SkyBottom.rgb, tt);

    float th = 1.0 - Coverage;
    float3 col = sky;
    float mask;

    if (Shape > 0.5)
    {
        float3 cB = PuffLayer(p, LayerB, Haze, sky, mask);
        col = lerp(col, cB, mask * step(1.5, LayerCount));
        float3 cA = PuffLayer(p, LayerA, 0.0, sky, mask);
        col = lerp(col, cA, mask);
    }
    else
    {
        float3 cC = CloudLayer(p, LayerC, th, Haze, sky, mask);
        col = lerp(col, cC, mask * step(2.5, LayerCount));
        float3 cB = CloudLayer(p, LayerB, th, Haze * 0.5, sky, mask);
        col = lerp(col, cB, mask * step(1.5, LayerCount));
        float3 cA = CloudLayer(p, LayerA, th, 0.0, sky, mask);
        col = lerp(col, cA, mask);
    }

    return float4(col, 1.0);
}

technique Sky
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
