#!/usr/bin/env python3
"""
Tạo Shaders/LiquidLens.ps (pixel shader ps_2_0 cho WPF) từ mã assembly bên dưới.

Vì sao có file này: trình biên dịch HLSL (fxc) chỉ có trong Windows SDK. File này tự lắp ráp bytecode D3D9 ps_2_0
để dự án không cần fxc khi build. Chạy:  python tools/make_liquid_lens.py
Nó (1) lắp ráp ra .ps, (2) giải mã lại và chạy thử bằng bộ mô phỏng, so với công thức tham chiếu,
(3) kiểm tra các giới hạn của ps_2_0 (số lệnh, số thanh ghi hằng đọc mỗi lệnh).
Bản HLSL tương đương nằm ở Shaders/LiquidLens.hlsl (nếu muốn tự biên dịch bằng fxc).

Thuật toán: kính lồi có viền cong. Với mỗi điểm trong hình chữ nhật bo góc, tính khoảng cách tới mép (SDF).
Gần mép, ảnh nền bị "kéo" từ sâu bên trong ra (phóng đại và nén ở vành), mỗi kênh R/G/B kéo một lượng khác nhau
(tán sắc màu), cộng ánh sáng phản chiếu ở vành và pha chút sương trắng.
"""
import struct, sys, os, re
import numpy as np

# ----------------------------------------------------------------------------- mã assembly ps_2_0
# Hằng do WPF gán:  c0=(Wt,Ht,0,0)  c1=(bx,by,R,1/bevel)  c2=(strength,chroma,spec,tint)  c3=(1/Wt,1/Ht,Lx,Ly)
ASM = """
ps_2_0
dcl t0.xy
dcl_2d s0
def c4, 0.5, 0.5, 1.0, 0.0
def c5, 1.0, -1.0, 0.0, 0.0
def c6, 0.0001, 0.45, 0.0, 0.0

add r0.xy, t0, -c4
mul r0.xy, r0, c0
abs r1.xy, r0
sub r1.xy, r1, c1
max r2.xy, r1, c4.wwww
mul r3.xy, r2, r2
add r3.x, r3.x, r3.y
add r3.x, r3.x, c6.x
rsq r3.y, r3.x
mul r3.z, r3.x, r3.y
max r3.w, r1.x, r1.y
min r3.w, r3.w, c4.w
add r3.z, r3.z, r3.w
sub r3.z, r3.z, c1.z
mov r4.x, -r3.z
add_sat r4.y, r4.x, c4.x
mul_sat r4.z, r4.x, c1.w
sub r4.z, c4.z, r4.z
mul r4.w, r4.z, r4.z
mul r5.z, r4.w, c2.x
cmp r5.xy, r0, c5.xxxx, c5.yyyy
mul r6.xy, r2, r5
mul r6.xy, r6, r3.yyyy
mul r7.xy, r6, c3
mul r7.xy, r7, r5.zzzz
sub r8.xy, t0, r7
mul r11.xy, r7, c2.yyyy
sub r9.xy, r8, r11
add r10.xy, r8, r11
texld r9, r9, s0
texld r8, r8, s0
texld r10, r10, s0
mov r0.x, r9.x
mov r0.y, r8.y
mov r0.z, r10.z
mul r11.x, r6.x, c3.z
mad r11.x, r6.y, c3.w, r11.x
mul r11.y, r4.z, r4.w
max r11.z, r11.x, c4.w
mov r11.w, -r11.x
max r11.w, r11.w, c4.w
mad r11.z, r11.w, c6.y, r11.z
mul r11.z, r11.z, r11.y
mul r11.z, r11.z, c2.z
add_sat r0.xyz, r0, r11.zzzz
mov r11.x, -c2.w
add r11.x, r11.x, c4.z
mad r0.xyz, r0, r11.xxxx, c2.wwww
mul r0.xyz, r0, r4.yyyy
mov r0.w, r4.y
mov oC0, r0
"""

OPCODES = {'mov': 1, 'add': 2, 'sub': 3, 'mad': 4, 'mul': 5, 'rcp': 6, 'rsq': 7, 'dp3': 8, 'dp4': 9,
           'min': 10, 'max': 11, 'abs': 35, 'cmp': 88, 'texld': 66}
NOPERANDS = {'mov': 2, 'add': 3, 'sub': 3, 'mad': 4, 'mul': 3, 'rcp': 2, 'rsq': 2, 'min': 3, 'max': 3,
             'abs': 2, 'cmp': 4, 'texld': 3}
TYPES = {'r': 0, 'v': 1, 'c': 2, 't': 3, 's': 10}
COMP = {'x': 0, 'y': 1, 'z': 2, 'w': 3, 'r': 0, 'g': 1, 'b': 2, 'a': 3}


def reg_bits(rtype, num):
    return 0x80000000 | (num & 0x7FF) | ((rtype & 7) << 28) | (((rtype >> 3) & 3) << 11)


def parse_reg(text):
    m = re.fullmatch(r'(-?)(oC|[rvcts])(\d+)(?:\.([xyzwrgba]+))?', text.strip())
    if not m:
        raise ValueError('toán hạng sai: ' + text)
    neg, kind, num, sw = m.groups()
    rtype = 8 if kind == 'oC' else TYPES[kind]
    return bool(neg), rtype, int(num), sw


def dest_token(text, sat):
    neg, rtype, num, sw = parse_reg(text)
    assert not neg
    mask = 0xF
    if sw:
        mask = 0
        for ch in sw:
            mask |= 1 << COMP[ch]
    return reg_bits(rtype, num) | (mask << 16) | ((1 if sat else 0) << 20)


def src_token(text):
    neg, rtype, num, sw = parse_reg(text)
    if not sw:
        comps = [0, 1, 2, 3]
    else:
        comps = [COMP[ch] for ch in sw]
        while len(comps) < 4:
            comps.append(comps[-1])
    swz = sum(c << (2 * i) for i, c in enumerate(comps))
    return reg_bits(rtype, num) | (swz << 16) | ((1 if neg else 0) << 24)


def assemble(asm):
    toks = [0xFFFF0200]  # ps_2_0
    for raw in asm.strip().splitlines():
        line = raw.split(';')[0].strip()
        if not line or line == 'ps_2_0':
            continue
        op, _, rest = line.partition(' ')
        args = [a.strip() for a in rest.split(',')] if rest else []
        if op == 'dcl':
            toks += [0x1F | (2 << 24), 0x80000000, dest_token(args[0], False)]
        elif op == 'dcl_2d':
            toks += [0x1F | (2 << 24), 0x90000000, dest_token(args[0], False)]
        elif op == 'def':
            toks += [0x51 | (5 << 24), dest_token(args[0], False)]
            toks += [struct.unpack('<I', struct.pack('<f', float(v)))[0] for v in args[1:]]
        else:
            sat = op.endswith('_sat')
            base = op[:-4] if sat else op
            n = NOPERANDS[base]
            assert len(args) == n, (line, n)
            toks.append(OPCODES[base] | (n << 24))
            toks.append(dest_token(args[0], sat))
            toks += [src_token(a) for a in args[1:]]
    toks.append(0x0000FFFF)
    return struct.pack('<%dI' % len(toks), *toks)


# ----------------------------------------------------------------------------- kiểm tra giới hạn ps_2_0
def check_limits(asm):
    arith = tex = 0
    problems = []
    for raw in asm.strip().splitlines():
        line = raw.split(';')[0].strip()
        if not line or line.startswith(('ps_', 'dcl', 'def')):
            continue
        op, _, rest = line.partition(' ')
        args = [a.strip() for a in rest.split(',')]
        base = op[:-4] if op.endswith('_sat') else op
        if base == 'texld':
            tex += 1
        else:
            arith += 1
        consts = {parse_reg(a)[2] for a in args[1:] if parse_reg(a)[1] == 2}
        if len(consts) > 1:
            problems.append('đọc nhiều thanh ghi hằng trong một lệnh: ' + line)
    temps = {parse_reg(a)[2] for raw in asm.splitlines() for a in re.findall(r'-?r\d+(?:\.\w+)?', raw.split(';')[0])}
    maxtemp = max(temps) + 1 if temps else 0
    if arith > 64: problems.append('quá 64 lệnh số học')
    if tex > 32: problems.append('quá 32 lệnh texture')
    if maxtemp > 12: problems.append('quá 12 thanh ghi tạm')
    return arith, tex, maxtemp, problems


# ----------------------------------------------------------------------------- bộ mô phỏng (giải mã độc lập từ bytes)
def simulate(blob, consts, uv, image):
    """Chạy bytecode trên N điểm. uv: (2,N); image: (H,W,4) float32; consts: {0:(4,),...}."""
    toks = list(struct.unpack('<%dI' % (len(blob) // 4), blob))
    assert toks[0] == 0xFFFF0200 and toks[-1] == 0x0000FFFF
    N = uv.shape[1]
    regs = {}
    cst = {k: np.array(v, np.float32) for k, v in consts.items()}
    t0 = np.zeros((4, N), np.float32); t0[0:2] = uv
    H, W = image.shape[:2]

    def sample(u, v):
        x = np.clip(u * W - 0.5, 0, W - 1); y = np.clip(v * H - 0.5, 0, H - 1)
        x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
        x1 = np.minimum(x0 + 1, W - 1); y1 = np.minimum(y0 + 1, H - 1)
        fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
        top = image[y0, x0] * (1 - fx) + image[y0, x1] * fx
        bot = image[y1, x0] * (1 - fx) + image[y1, x1] * fx
        return (top * (1 - fy) + bot * fy).T  # (4,N)

    def decode(tok):
        rtype = ((tok >> 28) & 7) | (((tok >> 11) & 3) << 3)
        return rtype, tok & 0x7FF

    def read(tok):
        rtype, num = decode(tok)
        swz = (tok >> 16) & 0xFF
        if rtype == 0: val = regs[num]
        elif rtype == 2: val = np.repeat(cst[num][:, None], N, 1)
        elif rtype == 3: val = t0
        else: raise ValueError('nguồn không hỗ trợ')
        out = np.stack([val[(swz >> (2 * i)) & 3] for i in range(4)])
        return -out if (tok >> 24) & 0xF == 1 else out

    def write(tok, val):
        rtype, num = decode(tok)
        mask = (tok >> 16) & 0xF
        if (tok >> 20) & 0xF & 1: val = np.clip(val, 0, 1)
        tgt = out_reg if rtype == 8 else regs.setdefault(num, np.zeros((4, N), np.float32))
        for i in range(4):
            if mask & (1 << i): tgt[i] = val[i]

    out_reg = np.zeros((4, N), np.float32)
    i = 1
    while toks[i] != 0x0000FFFF:
        tok = toks[i]; op = tok & 0xFFFF; ln = (tok >> 24) & 0xF
        ops = toks[i + 1:i + 1 + ln]; i += 1 + ln
        if op == 0x1F: continue
        if op == 0x51:  # def cN, a, b, c, d
            cst[decode(ops[0])[1]] = np.array(struct.unpack('<4f', struct.pack('<4I', *ops[1:5])), np.float32)
            continue
        s = [read(t) for t in (ops[1:2] if op == 66 else ops[1:])]  # texld: toán hạng 2 là sampler, không đọc như thanh ghi
        if op == 1: r = s[0]
        elif op == 2: r = s[0] + s[1]
        elif op == 3: r = s[0] - s[1]
        elif op == 4: r = s[0] * s[1] + s[2]
        elif op == 5: r = s[0] * s[1]
        elif op == 7: r = 1.0 / np.sqrt(np.abs(s[0]))
        elif op == 10: r = np.minimum(s[0], s[1])
        elif op == 11: r = np.maximum(s[0], s[1])
        elif op == 35: r = np.abs(s[0])
        elif op == 88: r = np.where(s[0] >= 0, s[1], s[2])
        elif op == 66: r = sample(s[0][0], s[0][1])
        else: raise ValueError('opcode chưa hỗ trợ: %d' % op)
        write(ops[0], r.astype(np.float32))
    return out_reg


# ----------------------------------------------------------------------------- công thức tham chiếu (viết độc lập, dạng "toán học")
def reference(uv, params, image):
    Wt, Ht, bx, by, R, invB, strength, chroma, spec, tint, Lx, Ly = params
    H, W = image.shape[:2]
    q = (uv.T - 0.5) * np.array([Wt, Ht])
    d = np.abs(q) - np.array([bx, by])
    dm = np.maximum(d, 0)
    ln = np.sqrt((dm ** 2).sum(1) + 1e-4)
    sdf = ln + np.minimum(np.maximum(d[:, 0], d[:, 1]), 0) - R
    e = -sdf
    alpha = np.clip(e + 0.5, 0, 1)
    w = 1 - np.clip(e * invB, 0, 1)
    M = strength * w * w
    n = dm * np.where(q >= 0, 1, -1) / ln[:, None]
    off = n / np.array([Wt, Ht]) * M[:, None]
    def samp(o):
        u = uv.T - o
        x = np.clip(u[:, 0] * W - 0.5, 0, W - 1); y = np.clip(u[:, 1] * H - 0.5, 0, H - 1)
        x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
        x1 = np.minimum(x0 + 1, W - 1); y1 = np.minimum(y0 + 1, H - 1)
        fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
        return (image[y0, x0] * (1 - fx) + image[y0, x1] * fx) * (1 - fy) + (image[y1, x0] * (1 - fx) + image[y1, x1] * fx) * fy
    sR = samp(off * (1 + chroma)); sG = samp(off); sB = samp(off * (1 - chroma))
    col = np.stack([sR[:, 0], sG[:, 1], sB[:, 2]], 1)
    dotL = n[:, 0] * Lx + n[:, 1] * Ly
    rim = w ** 3
    s = (np.maximum(dotL, 0) + 0.45 * np.maximum(-dotL, 0)) * rim * spec
    col = col + s[:, None]
    col = col * (1 - tint) + tint
    col = np.clip(col, 0, 1) * alpha[:, None]  # WPF/GPU kẹp [0,1] ở đầu ra
    return np.concatenate([col, alpha[:, None]], 1)


def lens_params(W, H, R, bevel=None, strength=None, chroma=0.22, spec=0.55, tint=0.10):
    R = min(R, W / 2, H / 2)
    bevel = bevel or min(R, 18.0)
    strength = strength if strength is not None else bevel * 1.0
    return dict(consts={0: (W, H, 0, 0), 1: (W / 2 - R, H / 2 - R, R, 1.0 / bevel),
                        2: (strength, chroma, spec, tint), 3: (1.0 / W, 1.0 / H, -0.62, -0.78)},
                ref=(W, H, W / 2 - R, H / 2 - R, R, 1.0 / bevel, strength, chroma, spec, tint, -0.62, -0.78))


def make_backdrop(w, h, seed=3):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    img = np.zeros((h, w, 4), np.float32); img[..., 3] = 1
    base = np.stack([0.07 + 0.10 * xx / w, 0.04 + 0.05 * yy / h, 0.07 + 0.08 * (1 - xx / w)], -1)
    img[..., :3] = base
    cols = [(0.90, 0.22, 0.28), (1.0, 0.72, 0.1), (0.95, 0.4, 0.7), (0.45, 0.35, 1.0), (0.1, 0.8, 0.7), (1.0, 0.9, 0.5)]
    for _ in range(26):
        cx, cy, r = rng.uniform(0, w), rng.uniform(0, h), rng.uniform(25, 95)
        c = np.array(cols[rng.integers(len(cols))]); a = rng.uniform(0.25, 0.8)
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
        m = np.clip(1.4 - d, 0, 1) ** 1.3 * a
        img[..., :3] = img[..., :3] * (1 - m[..., None]) + c * m[..., None]
    return np.clip(img, 0, 1)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    blob = assemble(ASM)
    arith, tex, temps, problems = check_limits(ASM)
    print(f'lệnh số học: {arith}/64, texture: {tex}/32, thanh ghi tạm: {temps}/12')
    if problems:
        print('VẤN ĐỀ:', *problems, sep='\n  '); sys.exit(1)

    # Kiểm tra: lắp ráp ví dụ kinh điển "mov oC0, c0" phải ra đúng chuỗi bytes mà fxc sinh ra
    sample = assemble('mov oC0, c0')
    expect = struct.pack('<5I', 0xFFFF0200, 0x02000001, 0x800F0800, 0xA0E40000, 0x0000FFFF)
    assert sample == expect, 'mã hóa cơ bản sai'
    print('mã hóa cơ bản: khớp với bytes chuẩn của fxc')

    # So bộ mô phỏng (chạy bytecode) với công thức tham chiếu
    img = make_backdrop(480, 160)
    worst = 0
    for (W, H, R) in [(400, 120, 28), (300, 44, 22), (360, 260, 20), (120, 60, 30)]:
        p = lens_params(W, H, R)
        ys, xs = np.mgrid[0:H, 0:W]
        uv = np.stack([((xs + 0.5) / W).ravel(), ((ys + 0.5) / H).ravel()]).astype(np.float32)
        sim = simulate(blob, p['consts'], uv, img)
        ref = reference(uv, p['ref'], img).T
        err = np.abs(sim - ref).max()
        worst = max(worst, err)
        print(f'  {W}x{H} R={R}: sai lệch lớn nhất mô phỏng vs tham chiếu = {err:.5f}')
    assert worst < 2e-3, 'bytecode không khớp công thức!'

    out = os.path.join(here, 'shader_experimental', 'LiquidLens.ps')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'wb') as f:
        f.write(blob)
    print('đã ghi', out, len(blob), 'bytes')

    # Ảnh xem thử (không đưa vào dự án)
    prev = os.path.join(here, 'preview_lens.png')
    from PIL import Image
    W, H, R = 420, 120, 30
    big = make_backdrop(560, 260)
    canvas = big.copy()
    p = lens_params(W, H, R)
    ys, xs = np.mgrid[0:H, 0:W]
    uv = np.stack([((xs + 0.5) / W).ravel(), ((ys + 0.5) / H).ravel()]).astype(np.float32)
    ox, oy = 70, 70
    crop = big[oy:oy + H, ox:ox + W]
    res = simulate(blob, p['consts'], uv, crop).T.reshape(H, W, 4)
    a = res[..., 3:4]
    canvas[oy:oy + H, ox:ox + W, :3] = res[..., :3] + crop[..., :3] * (1 - a)
    Image.fromarray((canvas[..., :3] * 255).astype(np.uint8)).resize((1120, 520)).save(prev)
    print('ảnh xem thử:', prev)


if __name__ == '__main__':
    main()
