// LiquidLens.hlsl - bản HLSL tương đương với bytecode trong LiquidLens.ps (tạo bằng tools/make_liquid_lens.py).
// Chỉ để đọc hiểu / tự biên dịch:  fxc /T ps_2_0 /E main /Fo LiquidLens.ps LiquidLens.hlsl
// Hằng do WPF (LiquidLensEffect) gán:
//   c0 = (rộng, cao) của hình, pixel      c1 = (nửa rộng - R, nửa cao - R, R, 1/độ dày vành)
//   c2 = (độ méo px, tán sắc, phản chiếu, sương trắng)      c3 = (1/rộng, 1/cao, hướng sáng x, hướng sáng y)

sampler2D Input : register(s0);
float4 TexSize  : register(c0);
float4 Shape    : register(c1);
float4 Look     : register(c2);
float4 Light    : register(c3);

float4 main(float2 uv : TEXCOORD0) : COLOR
{
    // Toạ độ pixel tính từ tâm hình, rồi khoảng cách có dấu tới hình chữ nhật bo góc (SDF)
    float2 q   = (uv - 0.5) * TexSize.xy;
    float2 d   = abs(q) - Shape.xy;
    float2 dm  = max(d, 0);
    float  len2 = dot(dm, dm) + 0.0001;
    float  rl  = rsqrt(len2);
    float  len = len2 * rl;
    float  sdf = len + min(max(d.x, d.y), 0) - Shape.z;
    float  e   = -sdf;                         // độ sâu tính từ mép vào trong

    float alpha = saturate(e + 0.5);           // mép mượt 1px
    float w     = 1 - saturate(e * Shape.w);   // 1 ở mép, 0 khi vào sâu hơn độ dày vành
    float w2    = w * w;
    float M     = Look.x * w2;                 // độ kéo ảnh (px)

    float2 sgn = (q >= 0) ? float2(1, 1) : float2(-1, -1);
    float2 n   = dm * sgn * rl;                // pháp tuyến hướng ra ngoài
    float2 off = n * Light.xy * M;             // đổi sang toạ độ uv

    // Mỗi kênh màu lấy mẫu lệch một lượng khác nhau -> tán sắc
    float2 uvG = uv - off;
    float2 dc  = off * Look.y;
    float4 sR  = tex2D(Input, uvG - dc);
    float4 sG  = tex2D(Input, uvG);
    float4 sB  = tex2D(Input, uvG + dc);
    float3 col = float3(sR.r, sG.g, sB.b);

    // Ánh sáng phản chiếu ở vành: mạnh phía nguồn sáng, yếu hơn ở phía đối diện
    float dotL = n.x * Light.z + n.y * Light.w;
    float rim  = w * w2;
    float spec = (max(dotL, 0) + 0.45 * max(-dotL, 0)) * rim * Look.z;
    col = saturate(col + spec);

    col = col * (1 - Look.w) + Look.w;         // sương trắng nhẹ
    return float4(col * alpha, alpha);         // màu nhân sẵn alpha (premultiplied)
}
