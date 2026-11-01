# maimai x NEEDY GIRL OVERDOSE 上机演示剪辑
# 不删片段，只变速；原声静音，只放 BGM；成片总长 = BGM 长度。
# 用法: python make_demo.py [输出文件名] [--plan 只打印时间线] [--plain 不套窗口]
import json, subprocess, sys
from pathlib import Path
import cv2
import numpy as np
import imageio_ffmpeg

FF = imageio_ffmpeg.get_ffmpeg_exe()
SRC = r"%USERPROFILE%\xwechat_files\wxid_l22ydqh3bzk312_0af2\msg\video\2026-10\222239240596bb350a28984f0e715dd9.mp4"
BGM = r"E:\claude workspace\NGO桌宠\素材源\9DM NEEDY GIRL OVERDOSE_完整解包_v2\06_游戏BGM素材\04_主题与开场\OP_PV.wav"
HERE = Path(__file__).parent
_names = [a for a in sys.argv[1:] if not a.startswith("--")]
OUT = HERE / (_names[0] if _names else "ngo_demo_v1.mp4")

FPS = 30
SW, SH = 544, 960          # 源分辨率
OW = 1080                  # 输出宽，lanczos 等比放大
BGM_LEN = 119.27           # OP_PV.wav 实测
FADE_IN, FADE_OUT = 0.5, 1.5
RAMP = 0.5                 # 速度切换处的过渡（输出秒）
WINDOW = "--plain" not in sys.argv   # 默认套 NGO 窗口（v2 起）；--plain 出 v1 那种直出
# 透视校正矩阵由 fit_rectify.py 拟合（机位全片不动）；CROP 是校正后坐标里的裁切 x, y, w, h：
# 去掉下方布帘；右下角校正后缺的几像素用边缘延展补
RECTIFY_H = HERE / "rectify_H.npy"
CROP = (62, -24, 468, 804)   # 以机台中轴 x=296 对称，上屏黑框左右贴住窗口边

# BGM 结构（OP_PV.wav，librosa 实测）：165 BPM，一小节 BAR 秒，第 0 小节重拍在 DOWNBEAT0 秒，4 小节一乐句。
# 前奏 0–20 / A 段 20–34 / 断拍 33.8 / 铺垫 35.0–46.6 / 副歌一 46.6–69.9 / 间奏 69.9–78.6 /
# 二段主歌 78.6–92（渐弱）/ 副歌二 93.1–116.3 / 116.3 起余音
DOWNBEAT0, BAR = 1.486, 1.4547


def bar(k):
    return DOWNBEAT0 + k * BAR


# 分段（源时间秒）。("fix", 速度) 不参与缩放；("var", 名义速度) 在相邻锚点之间按比例缩放凑时长。
SEGMENTS = [
    (0.0,    3.0,    "fix", 1.0,  "开机"),
    (3.0,    45.27,  "var", 8.0,  "Windose 封面 + 登录卡打字 kangelkawaii"),
    (45.27,  63.0,   "fix", 1.0,  "NGO 标题 → 刷卡（Ame/超天酱）→ 确认登录 → 换场"),
    (63.0,   77.8,   "var", 3.0,  "玩家信息、请稍等"),
    (77.8,   79.2,   "fix", 1.0,  "换场进选模式"),
    (79.2,   108.87, "var", 2.5,  "选模式，翻到 FREEDOM"),
    (108.87, 112.0,  "fix", 1.0,  "第一次换场动画"),
    (112.0,  120.3,  "var", 3.0,  "FREEDOM 规则弹窗"),
    (120.3,  123.13, "fix", 1.25, "换场进选歌"),
    (123.13, 128.0,  "fix", 1.0,  "选歌：伙伴卡、队长、Live2D"),
    (128.0,  148.5,  "var", 3.0,  "翻歌到 きゅびずむ"),
    (148.5,  157.0,  "var", 1.5,  "选曲、难度大弧"),
    (157.0,  173.83, "fix", 1.0,  "菜单 → 换场 → 糖糖走路"),
    (173.83, 183.73, "fix", 1.0,  "打歌"),
    (183.73, 188.0,  "fix", 1.0,  "超天酱换场"),
    (188.0,  198.6,  "var", 4.0,  "结算页"),
    (198.6,  201.2,  "var", 1.5,  "换场"),
    (201.2,  208.5,  "var", 4.0,  "回选歌"),
    (208.5,  210.5,  "var", 1.5,  "换场"),
    (210.5,  218.57, "var", 3.0,  "收藏页"),
    (218.57, 224.0,  "fix", 1.0,  "换场 + 服务器通信页（三人走轨道）"),
    (224.0,  233.0,  "var", 1.5,  "续关 CONTINUE?"),
    (233.0,  242.0,  "fix", 1.0,  "换场 → SEE YOU 三人 → 换场"),
    (242.0,  243.43, "var", 1.5,  "黑屏、开机标志"),
    (243.43, 244.33, "fix", 1.0,  "NGO 标题"),
    (244.33, 246.0,  "fix", 1.5,  "换场"),
    (246.0,  260.07, "var", 3.0,  "回到待机、刷卡页"),
]

# 锚点：画面上的硬切/换场（源秒，逐帧差分定位）钉在 BGM 的小节线上；1× 段不动，只缩放锚点之间的变速段。
ANCHORS = [
    (0.0,    0.0,     "开头"),
    (45.27,  bar(4),  "Windose → NGO 开机：前奏第 2 乐句"),
    (77.8,   bar(19), "进选模式换场：A 段第 2 乐句"),
    (108.87, bar(25), "第一次换场：铺垫段第 3 小节"),
    (123.13, bar(31), "选歌界面揭开、Live2D 登场：副歌一"),
    (183.73, bar(57), "超天酱换场：二段主歌"),
    (218.57, bar(63), "换场进服务器通信页（三人走轨道）：副歌二"),
    (243.43, bar(78), "NGO 标题出现：最后一小节"),
    (260.07, BGM_LEN, "结尾"),
]


def solve_speeds():
    speeds = {}
    for (s0, o0, _), (s1, o1, _) in zip(ANCHORS, ANCHORS[1:]):
        segs = [s for s in SEGMENTS if s0 <= s[0] and s[1] <= s1]
        fixed = sum((b - a) / v for a, b, k, v, _ in segs if k == "fix")
        nominal = sum((b - a) / v for a, b, k, v, _ in segs if k == "var")
        scale = nominal / ((o1 - o0) - fixed)
        for s in segs:
            speeds[s[0]] = s[3] * scale if s[2] == "var" else s[3]
    return speeds


def build_map(speeds):
    n_out = int(round(BGM_LEN * FPS))
    t_out = (np.arange(n_out) + 0.5) / FPS
    o, rows = 0.0, []
    for a, b, k, v, label in SEGMENTS:
        sp = speeds[a]
        rows.append(dict(out_start=o, out_end=o + (b - a) / sp, src_start=a, src_end=b, speed=sp, kind=k, label=label))
        o = rows[-1]["out_end"]

    def edge(r0, r1):
        # 段交界处的速度：挨着 1× 等固定段就取固定段速度，两个变速段之间取平均
        if r0["kind"] == "fix":
            return r0["speed"]
        return r1["speed"] if r1["kind"] == "fix" else (r0["speed"] + r1["speed"]) / 2

    # 速度过渡只做在变速段内部（两端各 RAMP 秒线性渐变），中段速度补偿，
    # 保证每段覆盖的源时长不变：固定段保持纯 1×，锚点落点不漂移。
    knots_t, knots_s = [0.0], [0.0]
    for i, r in enumerate(rows):
        d, src_len = r["out_end"] - r["out_start"], r["src_end"] - r["src_start"]
        u = np.linspace(0, d, max(int(d * 600), 2))
        if r["kind"] == "fix":
            s = r["speed"] * u
        else:
            v0 = edge(rows[i - 1], r) if i > 0 else r["speed"]
            v1 = edge(r, rows[i + 1]) if i + 1 < len(rows) else r["speed"]
            rr = min(RAMP, d / 3)
            x = (src_len - rr * (v0 + v1) / 2) / (d - rr)
            pace = np.interp(u, [0, rr, d - rr, d], [v0, x, x, v1])
            s = np.concatenate([[0], np.cumsum((pace[1:] + pace[:-1]) / 2 * np.diff(u))])
            s *= src_len / s[-1]
        knots_t += list(r["out_start"] + u[1:])
        knots_s += list(r["src_start"] + s[1:])
    src_t = np.interp(t_out - 0.5 / FPS, knots_t, knots_s)
    return src_t, rows


def render(src_t):
    idx = np.minimum(np.round(src_t * FPS).astype(int), 7801)
    dec = subprocess.Popen([FF, "-v", "error", "-i", SRC, "-an", "-f", "rawvideo",
                            "-pix_fmt", "rgb24", "-"], stdout=subprocess.PIPE,
                           stderr=subprocess.DEVNULL)  # 取到最后一帧就关管道，解码端的 broken pipe 属正常
    T = len(idx) / FPS
    fades = f"fade=t=in:st=0:d={FADE_IN},fade=t=out:st={T - FADE_OUT:.3f}:d={FADE_OUT},format=yuv420p"
    extra_in, warp, in_w, in_h = [], None, SW, SH
    if WINDOW:
        # 透视校正 + 裁切 + 放大一次 warp 完成，再贴进 make_window_bg.py 拼的 Windose 窗口内容区
        c = json.loads((HERE / "window_bg.json").read_text(encoding="utf-8"))
        x, y, w, h = CROP
        in_w, in_h = c["w"], c["h"]
        M = np.diag([in_w / w, in_h / h, 1.0]) @ np.array([[1, 0, -x], [0, 1, -y], [0, 0, 1]]) @ np.load(RECTIFY_H)
        warp = lambda f: cv2.warpPerspective(f, M, (in_w, in_h), flags=cv2.INTER_LANCZOS4,
                                             borderMode=cv2.BORDER_REPLICATE)
        fc = f"[2:v][0:v]overlay={c['x']}:{c['y']}:shortest=1,{fades}[out]"
        extra_in = ["-loop", "1", "-framerate", str(FPS), "-i", str(HERE / "window_bg.png")]
    else:
        fc = f"[0:v]scale={OW}:-2:flags=lanczos,{fades}[out]"
    af = f"afade=t=in:st=0:d={FADE_IN},afade=t=out:st={T - FADE_OUT:.3f}:d={FADE_OUT}"
    enc = subprocess.Popen([FF, "-y", "-v", "error",
                            "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{in_w}x{in_h}", "-r", str(FPS), "-i", "-",
                            "-i", BGM, *extra_in, "-filter_complex", fc, "-map", "[out]", "-map", "1:a",
                            "-af", af, "-t", f"{T:.3f}",
                            "-c:v", "libx264", "-preset", "slow", "-crf", "18",
                            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", str(OUT)],
                           stdin=subprocess.PIPE)
    fsize = SW * SH * 3
    cur_i, cur = -1, None
    for want in idx:
        while cur_i < want:
            buf = dec.stdout.read(fsize)
            if len(buf) < fsize:
                break
            cur, cur_i = buf, cur_i + 1
            if warp is not None:
                cur = warp(np.frombuffer(cur, np.uint8).reshape(SH, SW, 3)).tobytes()
        enc.stdin.write(cur)
    enc.stdin.close()
    dec.stdout.close()
    dec.wait()
    enc.wait()


def fmt(t):
    return f"{int(t // 60)}:{t % 60:04.1f}"


if __name__ == "__main__":
    speeds = solve_speeds()
    src_t, rows = build_map(speeds)
    t_frame = np.arange(len(src_t)) / FPS
    for a_src, a_out, note in ANCHORS[1:-1]:
        print(f"锚点 {note}: 目标 {a_out:.2f}s 实际 {np.interp(a_src, src_t, t_frame):.2f}s")
    print(f"成片最后一帧对应源 {src_t[-1]:.2f}s")
    lines = ["| 成片 | 原视频 | 速度 | 内容 |", "|---|---|---|---|"]
    for r in rows:
        lines.append(f"| {fmt(r['out_start'])}–{fmt(r['out_end'])} | {fmt(r['src_start'])}–{fmt(r['src_end'])} "
                     f"| {r['speed']:.2g}× | {r['label']} |")
    table = "\n".join(lines)
    print(table)
    (OUT.with_suffix(".timeline.md")).write_text(table + "\n", encoding="utf-8")
    (OUT.with_suffix(".timeline.json")).write_text(json.dumps(rows, ensure_ascii=False, indent=1), encoding="utf-8")
    if "--plan" not in sys.argv:
        render(src_t)
        print("done", OUT)
