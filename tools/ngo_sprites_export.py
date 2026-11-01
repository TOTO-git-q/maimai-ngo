"""Export the NGO pair drawings as straight-alpha, bottom-up BC7 packs (drawn in game with Sprites/Default, which premultiplies).

All source assets are read-only. Outputs are confined to stage/ngo_pair_sprites.
"""
import json
import math
import subprocess
import sys
from functools import lru_cache
from pathlib import Path

import cv2
import etcpak
import imageio_ffmpeg
import numpy as np
import texture2ddecoder

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(r"E:\cxref\ngo-motion-v2\work\sprites\drawings")
DEST = ROOT / "stage" / "ngo_pair_sprites"
QA = DEST / "_qa"
LAYOUT = json.loads((ROOT / "tasks/sprite_pair/pair_layout.json").read_text(encoding="utf-8"))
TIERS = {"13": "Fail", "14": "Clear", "15": "Good", "16": "Top"}
QA_NAMES = {"1-K-15s", "1-Ame-15s", "2-K-1", "2-Ame-1", "tap-K-A", "tap-Ame-A"}


def classify(name):
    parts = name.split("-")
    character = parts[1]
    if parts[0] == "1":
        group, tier = "Idle", None
    elif parts[0] == "2":
        group, tier = "Random", None
    elif parts[0] == "tap":
        group, tier = "Tap", None
    elif parts[0] == "7":
        group, tier = "Confirm", None
    elif parts[0] == "8":
        group, tier = "Enter", None
    else:
        tier = TIERS[parts[0]]
        group = "Result" + tier + ("Loop" if parts[2][0] == "L" else "Reaction")
    return character, group, tier


def load_rgb(path):
    bgr = cv2.imread(str(path), cv2.IMREAD_COLOR)
    if bgr is None:
        raise ValueError("cannot read " + str(path))
    return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)


def foreground(rgb):
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    return ~((hsv[:, :, 0] > 40) & (hsv[:, :, 0] < 85) &
             (hsv[:, :, 1] > 120) & (hsv[:, :, 2] > 80))


def keyed(rgb, fg):
    # Feather the foreground inside a two-pixel silhouette band. Fully keyed
    # green remains transparent. Despill is limited to the same boundary band.
    dist = cv2.distanceTransform(fg.astype(np.uint8), cv2.DIST_L2, 3)
    alpha = np.minimum(dist / 2.0, 1.0)
    color = rgb.copy()
    edge = dist < 2.1
    limit = np.maximum(color[:, :, 0], color[:, :, 2])
    color[:, :, 1][edge] = np.minimum(color[:, :, 1][edge], limit[edge])
    # Straight alpha: bleed the nearest foreground colour into transparent pixels so bilinear sampling at the
    # silhouette never pulls in black or green.
    if (~fg).any() and fg.any():
        _, labels = cv2.distanceTransformWithLabels((~fg).astype(np.uint8), cv2.DIST_L2, 3, labelType=cv2.DIST_LABEL_PIXEL)
        ys, xs = np.nonzero(fg)
        bg = ~fg
        idx = labels[bg] - 1
        color[bg] = color[ys[idx], xs[idx]]
    rgba = np.empty((*fg.shape, 4), dtype=np.uint8)
    rgba[:, :, :3] = color
    rgba[:, :, 3] = np.rint(alpha * 255).astype(np.uint8)
    return rgba


def decode(clip, drawing):
    with (DEST / (clip["name"] + ".bin")).open("rb") as f:
        f.seek(drawing["offset"])
        data = f.read(drawing["length"])
    w, h = clip["textureWidth"], clip["textureHeight"]
    out = texture2ddecoder.decode_bc7(data, w, h)
    return np.frombuffer(out, np.uint8).reshape(h, w, 4)[::-1, :, [2, 1, 0, 3]].copy()


def composite(rgba, background):
    a = rgba[:, :, 3:4].astype(np.float32) / 255.0
    return np.clip(rgba[:, :, :3].astype(np.float32) * a + background * (1 - a), 0, 255).astype(np.uint8)


def qa_key(clip, selected):
    rows = []
    for i in selected:
        rgba = decode(clip, clip["drawings"][i])
        gray, white = composite(rgba, 128), composite(rgba, 255)
        # Pick an edge with the greatest number of mixed-alpha pixels; biased
        # toward the upper half, where hair and ribbons normally occur.
        # Restrict the selection to the outer silhouette: BC7 may introduce
        # slightly translucent pixels inside solid clothing as well.
        solid = (rgba[:, :, 3] > 128).astype(np.uint8)
        edge = cv2.morphologyEx(solid, cv2.MORPH_GRADIENT, np.ones((3, 3), np.uint8))
        density = cv2.boxFilter(edge.astype(np.float32), -1, (48, 48))
        density[density.shape[0] // 3:, :] = 0
        y, x = np.unravel_index(np.argmax(density), density.shape)
        x = max(0, min(x - 24, max(0, rgba.shape[1] - 48)))
        y = max(0, min(y - 24, max(0, rgba.shape[0] - 48)))
        zoom = cv2.resize(gray[y:y + 48, x:x + 48], (144, 144), interpolation=cv2.INTER_NEAREST)
        row = np.full((max(gray.shape[0], 144), gray.shape[1] * 2 + 144, 3), 128, np.uint8)
        row[:gray.shape[0], :gray.shape[1]] = gray
        row[:white.shape[0], gray.shape[1]:2 * gray.shape[1]] = white
        row[:144, 2 * gray.shape[1]:] = zoom
        rows.append(row)
    out = np.concatenate(rows, axis=0)
    cv2.imwrite(str(QA / ("keying_" + clip["name"] + ".jpg")), cv2.cvtColor(out, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 95])


def encode_clip(folder):
    timing = json.loads((folder / "timing.json").read_text(encoding="utf-8"))
    name = timing["name"]
    character, group, tier = classify(name)
    expected = tuple(LAYOUT[character]["canvas"])
    box = None
    first_mask, max_diff, diff_i = None, -1, 0
    for i, item in enumerate(timing["drawings"]):
        rgb = load_rgb(folder / item["file"])
        if (rgb.shape[1], rgb.shape[0]) != expected:
            raise ValueError("wrong canvas: " + str(folder / item["file"]))
        mask = foreground(rgb)
        points = cv2.findNonZero(mask.astype(np.uint8))
        if points is None:
            raise ValueError("empty silhouette: " + name)
        x, y, w, h = cv2.boundingRect(points)
        if box is None:
            box = [x, y, x + w, y + h]
            first_mask = mask
        else:
            box = [min(box[0], x), min(box[1], y), max(box[2], x + w), max(box[3], y + h)]
            if name in QA_NAMES:
                diff = np.count_nonzero(mask != first_mask)
                if diff > max_diff:
                    max_diff, diff_i = diff, i
    x0, y0 = max(0, box[0] - 2), max(0, box[1] - 2)
    x1, y1 = min(expected[0], box[2] + 2), min(expected[1], box[3] + 2)
    w, h = (x1 - x0 + 3) // 4 * 4, (y1 - y0 + 3) // 4 * 4
    matrix = LAYOUT[character]["matrix"]
    clip = {"name": name, "character": character, "group": group, "tier": tier,
            "fps": timing["fps"], "totalFrames": timing["frames"], "textureWidth": w,
            "textureHeight": h, "rect": [matrix[0][0] * x0 + matrix[0][2],
            matrix[1][1] * y0 + matrix[1][2], matrix[0][0] * w, matrix[1][1] * h], "drawings": []}
    if timing["fps"] != 24 or sum(item["frames"] for item in timing["drawings"]) != timing["frames"]:
        raise ValueError("invalid timing: " + name)
    with (DEST / (name + ".bin")).open("wb") as pack:
        for item in timing["drawings"]:
            rgb = load_rgb(folder / item["file"])
            rgba = keyed(rgb, foreground(rgb))[y0:y1, x0:x1]
            padded = np.zeros((h, w, 4), np.uint8)
            padded[:rgba.shape[0], :rgba.shape[1]] = rgba
            payload = etcpak.compress_bc7(np.ascontiguousarray(padded[::-1]).tobytes(), w, h)
            if len(payload) != w * h:
                raise ValueError("invalid BC7 payload length")
            clip["drawings"].append({"offset": pack.tell(), "length": len(payload), "hold": item["frames"]})
            pack.write(payload)
    # Compare both orientations with the encoded source. This detects a row
    # reversal without depending on which end contains the character's face.
    reference = np.zeros((h, w, 4), np.uint8)
    rgb = load_rgb(folder / timing["drawings"][0]["file"])
    rgba = keyed(rgb, foreground(rgb))[y0:y1, x0:x1]
    reference[:rgba.shape[0], :rgba.shape[1]] = rgba
    decoded = decode(clip, clip["drawings"][0])
    err = np.mean(np.abs(decoded.astype(np.int16) - reference.astype(np.int16)))
    flipped_err = np.mean(np.abs(decoded[::-1].astype(np.int16) - reference.astype(np.int16)))
    if err >= flipped_err:
        raise ValueError("BC7 orientation check failed: " + name)
    if name in QA_NAMES:
        qa_key(clip, [0, len(clip["drawings"]) // 2, diff_i])
    print(name, len(clip["drawings"]), w, h, "orientation error", round(err, 2), flush=True)
    return clip


@lru_cache(maxsize=12)
def preview_layer(name, index, x, y, w, h, tw, th, offset, length):
    clip = {"name": name, "textureWidth": tw, "textureHeight": th}
    rgba = decode(clip, {"offset": offset, "length": length}).astype(np.float32)
    matrix = np.array([[w / tw, 0, x], [0, h / th, y]], np.float32)
    return cv2.warpAffine(rgba, matrix, (1024, 1024), flags=cv2.INTER_LINEAR)


def pair_image(layers, bg=128):
    canvas = np.full((1024, 1024, 3), bg, np.float32)
    # Same as the game: per character (Ame, then K) the outgoing drawing at full opacity, then the incoming one
    # over it at the fade weight (straight alpha, source-over).
    for character in ("Ame", "K"):
        for clip, idx, weight in layers:
            if clip["character"] != character:
                continue
            x, y, w, h = clip["rect"]
            drawing = clip["drawings"][idx]
            rgba = preview_layer(clip["name"], idx, x, y, w, h, clip["textureWidth"],
                                 clip["textureHeight"], drawing["offset"], drawing["length"])
            a = rgba[:, :, 3:4] / 255.0 * weight
            canvas = rgba[:, :, :3] * a + canvas * (1 - a)
    return np.clip(canvas, 0, 255).astype(np.uint8)


def frame_index(clip, elapsed, loop):
    n = int(elapsed * 24)
    n = n % clip["totalFrames"] if loop else min(n, clip["totalFrames"] - 1)
    for i, item in enumerate(clip["drawings"]):
        n -= item["hold"]
        if n < 0:
            return i
    raise ValueError("timeline mismatch")


def qa_pair_and_preview(clips):
    by_name = {c["name"]: c for c in clips}
    idle_a, idle_k = by_name["1-Ame-15s"], by_name["1-K-15s"]
    pair = pair_image([(idle_a, 0, 1), (idle_k, 0, 1)], 0)
    ref = load_rgb(ROOT / "tasks/sprite_pair/pair_reference.png")
    # Compare on the source's green background to retain a meaningful diff.
    canvas = np.zeros((1024, 1024, 4), np.float32)
    for clip in (idle_a, idle_k):
        rgba = decode(clip, clip["drawings"][0]).astype(np.float32)
        x, y, w, h = clip["rect"]
        m = np.array([[w / clip["textureWidth"], 0, x], [0, h / clip["textureHeight"], y]], np.float32)
        warped = cv2.warpAffine(rgba, m, (1024, 1024))
        canvas[:, :, :3] = warped[:, :, :3] * (warped[:, :, 3:4] / 255) + canvas[:, :, :3] * (1 - warped[:, :, 3:4] / 255)
        canvas[:, :, 3:4] = warped[:, :, 3:4] + canvas[:, :, 3:4] * (1 - warped[:, :, 3:4] / 255)
    # Use green from the reference corners.
    green = np.median(np.concatenate([ref[:32, :32].reshape(-1, 3), ref[:32, -32:].reshape(-1, 3)]), axis=0)
    pair = np.clip(canvas[:, :, :3] + green * (1 - canvas[:, :, 3:4] / 255), 0, 255).astype(np.uint8)
    triptych = np.concatenate([pair, ref, cv2.absdiff(pair, ref)], axis=1)
    cv2.imwrite(str(QA / "pair_rest.png"), cv2.cvtColor(triptych, cv2.COLOR_RGB2BGR))

    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [ffmpeg, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "1024x1024", "-r", "24", "-i", "-", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", str(QA / "preview_select.mp4")]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    events = {"K": [(4., by_name["2-K-1"]), (14., by_name["tap-K-A"])],
              "Ame": [(9., by_name["2-Ame-1"]), (14.1, by_name["tap-Ame-A"])]}
    schedules = {}
    for character, idle in (("Ame", idle_a), ("K", idle_k)):
        segments = [(0., idle, None, 0)]
        idle_start = 0.
        for at, clip in events[character]:
            old_idx = frame_index(idle, max(0., at - idle_start - .12), True)
            segments.append((at, clip, idle, old_idx))
            idle_start = at + .12 + clip["totalFrames"] / 24
            segments.append((idle_start, idle, clip, len(clip["drawings"]) - 1))
        schedules[character] = segments
    try:
        for frame in range(20 * 24):
            t = frame / 24
            layers = []
            for character, idle in (("Ame", idle_a), ("K", idle_k)):
                current, start, previous, old_idx = idle, 0., None, 0
                for at, clip, old, old_frame in schedules[character]:
                    if t < at:
                        break
                    current, start, previous, old_idx = clip, at, old, old_frame
                idx = frame_index(current, max(0., t - start - (.12 if previous else 0.)), current is idle)
                fade = min(1., max(0., (t - start) / .12)) if previous else 1.
                if fade < 1:
                    layers.append((previous, old_idx, 1.))
                layers.append((current, idx, fade))
            proc.stdin.write(pair_image(layers).tobytes())
    finally:
        proc.stdin.close()
    if proc.wait() != 0:
        raise RuntimeError("ffmpeg failed")


def print_sizes(clips):
    print("Total pack bytes:", sum((DEST / (c["name"] + ".bin")).stat().st_size for c in clips))
    for page, pred in [("select", lambda c: c["tier"] is None)] + [(t, lambda c, t=t: c["tier"] == t) for t in TIERS.values()]:
        print(page, "VRAM bytes:", sum(c["textureWidth"] * c["textureHeight"] * len(c["drawings"]) for c in clips if pred(c)))


def main():
    QA.mkdir(parents=True, exist_ok=True)
    folders = sorted(p for p in SOURCE.iterdir() if p.is_dir() and (p / "timing.json").is_file())
    if len(folders) != 50:
        raise ValueError("expected 50 clips, found " + str(len(folders)))
    if sys.argv[1:] == ["--qa-only"]:
        clips = json.loads((DEST / "manifest.json").read_text(encoding="utf-8"))["clips"]
        for clip in clips:
            if clip["name"] not in QA_NAMES:
                continue
            folder = SOURCE / clip["name"]
            timing = json.loads((folder / "timing.json").read_text(encoding="utf-8"))
            masks = [foreground(load_rgb(folder / d["file"])) for d in timing["drawings"]]
            most = max(range(len(masks)), key=lambda i: np.count_nonzero(masks[i] != masks[0]))
            qa_key(clip, [0, len(masks) // 2, most])
    elif not sys.argv[1:]:
        clips = [encode_clip(p) for p in folders]
        manifest = {"version": 1, "alpha": "straight", "pairCanvas": [1024, 1024], "drawOrder": ["Ame", "K"], "crossfadeSeconds": .12, "clips": clips}
        (DEST / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    else:
        raise ValueError("usage: ngo_sprites_export.py [--qa-only]")
    qa_pair_and_preview(clips)
    print_sizes(clips)


if __name__ == "__main__":
    main()
