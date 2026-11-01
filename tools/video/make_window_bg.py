# 用 NGO 解包原版精灵拼 Windose 窗口底图（壁纸 + 窗口 + -口x 按钮），视频贴进窗口内容区。
# 输出 window_bg.png（1080x1920）和 window_bg.json（内容区位置/尺寸）。
import json
from pathlib import Path
import numpy as np
from PIL import Image

NGO = Path(r"E:\claude workspace\NGO桌宠\素材源\9DM NEEDY GIRL OVERDOSE_完整解包_v2")
SPR = NGO / "01_Unity资源" / "Sprite"
WALL = NGO / "05_桌面壁纸素材" / "FHDbg_Sprite_1920x1080.png"
HERE = Path(__file__).parent

CW, CH = 1080, 1920        # 画布
S = 3                      # 窗口像素放大倍数（最近邻）
BODY_W = 312               # 窗口本体宽（精灵像素）
CROP_W, CROP_H = 468, 804  # 透视校正后的裁切尺寸（= make_demo.py 的 CROP，决定内容区高宽比）


def sprite(name):
    return np.asarray(Image.open(SPR / f"{name}.png").convert("RGBA"))


def nine_slice(a, T, H):
    # windowbase_active 300x200：本体 294x196 + 右/下 4px 半透明投影；中段整列/整行复制拉伸
    cm = np.array([x if x < 70 else (280 + x - (T - 20) if x >= T - 20 else 150) for x in range(T)])
    rm = np.array([y if y < 50 else (170 + y - (H - 30) if y >= H - 30 else 100) for y in range(H)])
    return a[rm][:, cm].copy()


def build():
    win = sprite("sharedassets0__30")          # windowbase_active
    btn_min = sprite("sharedassets0__31")      # button_minimize
    btn_max = sprite("sharedassets0__18")      # button_maximize
    btn_close = sprite("sharedassets0__24")    # button_close

    content_w = (BODY_W - 16) * S
    content_hu = int(np.ceil(content_w * CROP_H / CROP_W / S))
    T = BODY_W + 6                             # 右侧含 4px 投影 + 2px 透明
    H = 42 + content_hu + 22                   # 内容区上方 42 行，下方 22 行（含投影）
    w = nine_slice(win, T, H)
    pink_r = T - 15                            # 标题栏粉色区最右列
    y = 11
    for i, b in enumerate((btn_close, btn_max, btn_min)):
        x = pink_r - 2 - 20 * (i + 1) - 2 * i
        blk = b.astype(float)
        al = blk[..., 3:4] / 255
        w[y:y + 20, x:x + 20, :3] = (blk[..., :3] * al + w[y:y + 20, x:x + 20, :3] * (1 - al)).astype(np.uint8)
    win_img = Image.fromarray(w).resize((T * S, H * S), Image.NEAREST)

    # 壁纸：原版 FHDbg 图案区周期 72px（8px 一格），缩到 6px 一格（=2S）后平铺
    wall = Image.open(WALL).convert("RGB").crop((248, 400, 320, 472)).resize((2 * S * 9,) * 2, Image.NEAREST)
    bg = Image.new("RGB", (CW, CH))
    for yy in range(0, CH, wall.height):
        for xx in range(0, CW, wall.width):
            bg.paste(wall, (xx, yy))

    wx = (CW - (T - 6) * S) // 2
    wy = (CH - (H - 4) * S) // 2
    bg.paste(win_img, (wx, wy), win_img)
    content = dict(x=wx + 8 * S, y=wy + 42 * S, w=content_w, h=content_hu * S)
    bg.save(HERE / "window_bg.png")
    (HERE / "window_bg.json").write_text(json.dumps(content), encoding="utf-8")
    return content


if __name__ == "__main__":
    print(build())
