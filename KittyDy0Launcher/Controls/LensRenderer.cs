namespace KittyDy0Launcher.Controls;

/// <summary>
/// Tính khúc xạ kính lỏng bằng CPU (C# thuần, không dùng pixel shader nên không thể làm sập launcher).
/// Công thức giống hệt bản shader đã thử nghiệm: với mỗi điểm trong hình chữ nhật bo góc, tính khoảng cách tới mép;
/// gần mép, ảnh nền bị kéo từ sâu bên trong ra (phóng đại và nén ở vành), mỗi kênh R/G/B kéo một lượng khác nhau
/// (tán sắc màu), cộng ánh sáng phản chiếu ở vành và pha chút sương trắng.
/// Chỉ vùng sát mép (độ dày "bevel") cần lấy mẫu lệch; phần bên trong chép thẳng nên rất nhanh.
/// </summary>
internal static class LensRenderer
{
    private const float StrengthK = 1.25f; // độ kéo ở mép = bevel * hệ số này
    private const float Chroma = 0.26f;    // độ lệch giữa các kênh màu
    private const float Spec = 0.55f;      // độ sáng phản chiếu ở vành

    /// <summary>Hệ số độ mạnh khúc xạ (1 = mặc định), do ThemeManager đặt theo cài đặt của người dùng.</summary>
    internal static float StrengthScale = 1f;

    /// <summary>Độ sương trắng phủ lên thấu kính (độ sáng kính).</summary>
    internal static float TintAmount = 0.06f;
    private const float Lx = -0.62f;       // hướng ánh sáng (từ trên-trái)
    private const float Ly = -0.78f;

    /// <summary>
    /// Dựng ảnh thấu kính vào <paramref name="dst"/> (Pbgra32, w*h*4 byte).
    /// </summary>
    /// <param name="src">Ảnh nền (Pbgra32, opaque) kích thước sw x sh.</param>
    /// <param name="cx">Toạ độ pixel góc trên-trái của thấu kính trên ảnh nền.</param>
    /// <param name="radius">Bán kính góc, pixel.</param>
    /// <param name="bevelMax">Độ dày vành tối đa, pixel.</param>
    public static void Render(byte[] src, int sw, int sh, int cx, int cy, int w, int h,
        float radius, float bevelMax, byte[] dst)
    {
        var r = Math.Max(1f, Math.Min(radius, Math.Min(w, h) / 2f));
        var bevel = Math.Max(2f, Math.Min(r, bevelMax));
        var invBevel = 1f / bevel;
        var strength = bevel * StrengthK * StrengthScale;
        var tint = TintAmount;
        float bx = w / 2f - r, by = h / 2f - r;

        for (var y = 0; y < h; y++)
        {
            var qy = y + 0.5f - h / 2f;
            var dy = MathF.Abs(qy) - by;
            var my = MathF.Max(dy, 0f);

            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                var qx = x + 0.5f - w / 2f;
                var dx = MathF.Abs(qx) - bx;
                var mx = MathF.Max(dx, 0f);

                // e = độ sâu tính từ mép vào trong (âm nghĩa là ở ngoài hình)
                var len = MathF.Sqrt(mx * mx + my * my + 1e-4f);
                var e = r - len - MathF.Min(MathF.Max(dx, dy), 0f);
                var alpha = Math.Clamp(e + 0.5f, 0f, 1f); // mép mượt 1px
                if (alpha <= 0f)
                {
                    dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0;
                    continue;
                }

                var wgt = 1f - Math.Clamp(e * invBevel, 0f, 1f); // 1 ở mép, 0 khi vào sâu hơn vành
                float rr, gg, bb;

                if (wgt <= 0f)
                {
                    // Vùng bên trong: chép thẳng điểm ảnh nền ngay phía dưới
                    var sx = Math.Clamp(cx + x, 0, sw - 1);
                    var sy = Math.Clamp(cy + y, 0, sh - 1);
                    var si = (sy * sw + sx) * 4;
                    bb = src[si] / 255f;
                    gg = src[si + 1] / 255f;
                    rr = src[si + 2] / 255f;
                }
                else
                {
                    var w2 = wgt * wgt;
                    var m = strength * w2; // độ kéo (pixel)
                    var nx = mx * (qx >= 0 ? 1f : -1f) / len; // pháp tuyến hướng ra ngoài
                    var ny = my * (qy >= 0 ? 1f : -1f) / len;
                    var ox = nx * m;
                    var oy = ny * m;
                    var px = cx + x + 0.5f;
                    var py = cy + y + 0.5f;

                    // Mỗi kênh lấy mẫu lệch một lượng khác nhau -> tán sắc
                    rr = Bilinear(src, sw, sh, px - ox * (1f + Chroma), py - oy * (1f + Chroma), 2);
                    gg = Bilinear(src, sw, sh, px - ox, py - oy, 1);
                    bb = Bilinear(src, sw, sh, px - ox * (1f - Chroma), py - oy * (1f - Chroma), 0);

                    // Ánh sáng phản chiếu ở vành: mạnh phía nguồn sáng, yếu hơn ở phía đối diện
                    var dotL = nx * Lx + ny * Ly;
                    var s = (MathF.Max(dotL, 0f) + 0.45f * MathF.Max(-dotL, 0f)) * (wgt * w2) * Spec;
                    rr = Math.Min(rr + s, 1f);
                    gg = Math.Min(gg + s, 1f);
                    bb = Math.Min(bb + s, 1f);
                }

                rr = rr * (1f - tint) + tint;
                gg = gg * (1f - tint) + tint;
                bb = bb * (1f - tint) + tint;

                // Màu nhân sẵn alpha (premultiplied)
                dst[o] = (byte)(bb * alpha * 255f + 0.5f);
                dst[o + 1] = (byte)(gg * alpha * 255f + 0.5f);
                dst[o + 2] = (byte)(rr * alpha * 255f + 0.5f);
                dst[o + 3] = (byte)(alpha * 255f + 0.5f);
            }
        }
    }

    /// <summary>Lấy mẫu song tuyến một kênh (0=B, 1=G, 2=R) tại toạ độ điểm ảnh (X, Y), kẹp trong ảnh.</summary>
    private static float Bilinear(byte[] src, int sw, int sh, float X, float Y, int ch)
    {
        var fx = Math.Clamp(X - 0.5f, 0f, sw - 1f);
        var fy = Math.Clamp(Y - 0.5f, 0f, sh - 1f);
        var x0 = (int)fx;
        var y0 = (int)fy;
        var x1 = Math.Min(x0 + 1, sw - 1);
        var y1 = Math.Min(y0 + 1, sh - 1);
        var tx = fx - x0;
        var ty = fy - y0;

        float a = src[(y0 * sw + x0) * 4 + ch];
        float b = src[(y0 * sw + x1) * 4 + ch];
        float c = src[(y1 * sw + x0) * 4 + ch];
        float d = src[(y1 * sw + x1) * 4 + ch];
        return ((a * (1f - tx) + b * tx) * (1f - ty) + (c * (1f - tx) + d * tx) * ty) / 255f;
    }
}
