"""NGO color ramps taken from NEEDY GIRL OVERDOSE's own UI sprites (Windose buttons / windows, JINE, stream room).
A maimai blue or green is replaced by the ramp color at its luminance, so the shading survives but every hue is
one NGO uses. NgoTint.cs carries the same ramps for the runtime vertex tint; keep the two in step."""
import numpy as np

# dark -> light
VIOLET = ["#4D23CF", "#815CDD", "#C3A6E3", "#F7E1FB", "#FFF8FF"]   # Windose close button, button border, lavender, button face
PINK = ["#9E3C8C", "#D46CC4", "#EE98EE", "#FCE1F1", "#FFF8FF"]     # JINE sky #EE98EE, login panel #FCE1F1


def _rgb(ramp):
    return np.array([[int(h[i:i + 2], 16) for i in (1, 3, 5)] for h in ramp], np.float32)


def ramp_at(ramp, t):
    """t in 0..1 (array) -> rgb along the ramp, linear between stops."""
    stops = _rgb(ramp)
    t = np.clip(t, 0, 1) * (len(stops) - 1)
    i = np.minimum(t.astype(int), len(stops) - 2)
    f = (t - i)[..., None]
    return stops[i] * (1 - f) + stops[i + 1] * f


def hue_class(rgb):
    """rgb float 0..255 (...,3) -> (blue mask, green mask) with maimai's hue bands; greys untouched."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(-1), rgb.min(-1)
    s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    sat = s >= 0.15
    return sat & (h >= 175) & (h < 255), sat & (h >= 80) & (h < 175)


def remap(rgb, lo=0.0, hi=1.0):
    """Replace blue by the violet ramp and green by the pink ramp at the pixel's luminance (mapped into lo..hi)."""
    rgb = rgb.astype(np.float32)
    lum = (0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]) / 255
    t = lo + (hi - lo) * lum
    blue, green = hue_class(rgb)
    out = rgb.copy()
    out[blue] = ramp_at(VIOLET, t)[blue]
    out[green] = ramp_at(PINK, t)[green]
    return out
