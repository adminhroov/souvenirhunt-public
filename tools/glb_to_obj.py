"""GLB(glTF 2.0 바이너리) → OBJ + MTL + 텍스처 — Unity가 .glb를 바로 읽지 못해서(10-13, Downloads ceramic_bowl.glb).

대상: AI 생성 모델에서 흔한 단순 구조 — 삼각형 프리미티브, POSITION/TEXCOORD_0 float, 인덱스 uint8/16/32,
노드 변환 없음, 텍스처는 GLB 안에 PNG/JPEG. 압축 확장(Draco·meshopt 등)이 있으면 중단한다.
법선이 없으면 OBJ에도 넣지 않는다(Unity 임포터가 계산). glTF UV는 위가 0이라 OBJ용으로 v를 뒤집는다.

사용: python tools/glb_to_obj.py <입력.glb> <출력폴더> <이름> [--pivot bottom|none] [--flip]
  --pivot bottom(기본): 바운즈 바닥 중앙을 원점으로 옮김(배치하기 쉽게) / none: 원본 좌표 그대로
  --flip: X축 기준 180° 회전(위아래 뒤집힘 — AI 생성 모델에서 흔함, 10-13 세라믹 그릇). 거울 반전이 아니라 회전이라 면 방향 유지
"""
import json
import os
import struct
import sys

COMP = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb(path):
    d = open(path, "rb").read()
    magic, ver, _ = struct.unpack_from("<4sII", d, 0)
    if magic != b"glTF" or ver != 2:
        sys.exit(f"glTF 2.0 바이너리가 아님: {magic} v{ver}")
    off, js, bin_ = 12, None, None
    while off < len(d):
        clen, ctype = struct.unpack_from("<I4s", d, off)
        body = d[off + 8: off + 8 + clen]
        if ctype == b"JSON":
            js = json.loads(body)
        elif ctype == b"BIN\x00":
            bin_ = body
        off += 8 + clen
    return js, bin_


def accessor(js, bin_, idx):
    a = js["accessors"][idx]
    bv = js["bufferViews"][a["bufferView"]]
    fmt, size = COMP[a["componentType"]]
    n = NCOMP[a["type"]]
    stride = bv.get("byteStride", size * n)
    base = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    out = []
    for i in range(a["count"]):
        vals = struct.unpack_from("<" + fmt * n, bin_, base + i * stride)
        out.append(vals if n > 1 else vals[0])
    return out


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    src, outdir, name = sys.argv[1], sys.argv[2], sys.argv[3]
    pivot = "none" if "--pivot" in sys.argv and sys.argv[sys.argv.index("--pivot") + 1] == "none" else "bottom"
    js, bin_ = read_glb(src)
    if js.get("extensionsRequired"):
        sys.exit(f"필수 확장 {js['extensionsRequired']} — 이 변환기로는 못 읽음(glTFast 패키지 등 필요)")
    for n in js.get("nodes", []):
        if any(k in n for k in ("matrix", "translation", "rotation", "scale")):
            print(f"⚠ 노드 '{n.get('name')}'에 변환이 있음 — 무시하고 메시 좌표 그대로 씀")
    os.makedirs(outdir, exist_ok=True)

    verts, uvs, faces, tris = [], [], [], 0
    for m in js["meshes"]:
        for pr in m["primitives"]:
            if pr.get("mode", 4) != 4:
                sys.exit("삼각형이 아닌 프리미티브")
            pos = accessor(js, bin_, pr["attributes"]["POSITION"])
            uv = accessor(js, bin_, pr["attributes"]["TEXCOORD_0"]) if "TEXCOORD_0" in pr["attributes"] else None
            idx = accessor(js, bin_, pr["indices"]) if "indices" in pr else list(range(len(pos)))
            base = len(verts)
            verts += pos
            uvs += uv if uv else [(0.0, 0.0)] * len(pos)
            for t in range(0, len(idx), 3):
                faces.append((base + idx[t], base + idx[t + 1], base + idx[t + 2]))
            tris += len(idx) // 3

    if "--flip" in sys.argv:
        verts = [(x, -y, -z) for x, y, z in verts]
    xs, ys, zs = zip(*verts)
    off =((min(xs) + max(xs)) / 2, min(ys), (min(zs) + max(zs)) / 2) if pivot == "bottom" else (0.0, 0.0, 0.0)

    # 텍스처(첫 이미지) 추출
    tex_name = None
    if js.get("images"):
        im = js["images"][0]
        bv = js["bufferViews"][im["bufferView"]]
        data = bin_[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
        ext = "png" if im.get("mimeType", "image/png") == "image/png" else "jpg"
        tex_name = f"{name}_basecolor.{ext}"
        open(os.path.join(outdir, tex_name), "wb").write(data)

    with open(os.path.join(outdir, name + ".mtl"), "w", encoding="utf-8", newline="\n") as f:
        f.write(f"newmtl {name}_Mat\nKd 1 1 1\n")
        if tex_name:
            f.write(f"map_Kd {tex_name}\n")
    with open(os.path.join(outdir, name + ".obj"), "w", encoding="utf-8", newline="\n") as f:
        f.write(f"# {os.path.basename(src)} → glb_to_obj.py, 삼각형 {tris}, 정점 {len(verts)}, 피벗 {pivot}{', 위아래 뒤집음' if '--flip' in sys.argv else ''}\n")
        f.write(f"mtllib {name}.mtl\no {name}\n")
        for x, y, z in verts:
            f.write(f"v {x - off[0]:.6f} {y - off[1]:.6f} {z - off[2]:.6f}\n")
        for u, v in uvs:
            f.write(f"vt {u:.6f} {1.0 - v:.6f}\n")
        f.write(f"usemtl {name}_Mat\n")
        for a, b, c in faces:
            f.write(f"f {a + 1}/{a + 1} {b + 1}/{b + 1} {c + 1}/{c + 1}\n")
    size = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    print(f"OK {name}: 삼각형 {tris}, 정점 {len(verts)}, 크기 {size[0]:.3f}×{size[1]:.3f}×{size[2]:.3f}m, 텍스처 {tex_name}, 피벗 {pivot}")


if __name__ == "__main__":
    main()
