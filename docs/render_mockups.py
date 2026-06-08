#!/usr/bin/env python3
"""Render mockups of the SplitPlay UI that mirror the implemented XAML layout
and theme. Output: docs/mockups/*.png. Drawn at 2x then downscaled for crisp text."""
import os
from PIL import Image, ImageDraw, ImageFont

S = 2  # supersampling
W, H = 1180, 760
OUT = os.path.join(os.path.dirname(__file__), "mockups")
os.makedirs(OUT, exist_ok=True)

FONT_DIR = "/usr/share/fonts/truetype/dejavu"
def font(name, size): return ImageFont.truetype(os.path.join(FONT_DIR, name), size * S)
REG = lambda s: font("DejaVuSans.ttf", s)
BOLD = lambda s: font("DejaVuSans-Bold.ttf", s)
MONO = lambda s: font("DejaVuSansMono.ttf", s)

# --- Theme palette (matches Themes/Theme.xaml) ---
C = {
    "bg": "#1B2027", "surface": "#232B34", "surfaceAlt": "#2B343E", "topbar": "#161B21",
    "accent": "#4FD1A5", "accentHover": "#63E0B6", "text": "#EAEFF4", "text2": "#93A1B0",
    "border": "#313B47", "danger": "#E05A5A", "dark": "#10241D",
}

def hx(c):
    c = C.get(c, c).lstrip("#")
    return tuple(int(c[i:i+2], 16) for i in (0, 2, 4))

class M:
    def __init__(self):
        self.img = Image.new("RGB", (W * S, H * S), hx("bg"))
        self.d = ImageDraw.Draw(self.img)
    def rect(self, x, y, w, h, fill=None, outline=None, width=1, r=0):
        box = [x*S, y*S, (x+w)*S, (y+h)*S]
        f = hx(fill) if fill else None
        o = hx(outline) if outline else None
        if r > 0:
            self.d.rounded_rectangle(box, radius=r*S, fill=f, outline=o, width=width*S)
        else:
            self.d.rectangle(box, fill=f, outline=o, width=width*S)
    def line(self, x1, y1, x2, y2, fill, width=1):
        self.d.line([x1*S, y1*S, x2*S, y2*S], fill=hx(fill), width=width*S)
    def text(self, x, y, s, f, fill, anchor="la"):
        self.d.text((x*S, y*S), s, font=f, fill=hx(fill), anchor=anchor)
    def textlen(self, s, f): return self.d.textlength(s, font=f) / S
    def vgrad(self, x, y, w, h, top, bottom, r=0):
        ct, cb = hx(top), hx(bottom)
        strip = Image.new("RGB", (1, h), 0)
        sd = strip.load()
        for i in range(h):
            t = i / max(1, h - 1)
            sd[0, i] = tuple(int(ct[k] + (cb[k] - ct[k]) * t) for k in range(3))
        strip = strip.resize((w * S, h * S))
        if r > 0:
            mask = Image.new("L", (w * S, h * S), 0)
            ImageDraw.Draw(mask).rounded_rectangle([0, 0, w*S-1, h*S-1], radius=r*S, fill=255)
            self.img.paste(strip, (x*S, y*S), mask)
        else:
            self.img.paste(strip, (x*S, y*S))
    def save(self, name):
        self.img.resize((W, H), Image.LANCZOS).save(os.path.join(OUT, name))
        print("wrote", name)

# ---------------------------------------------------------------- top bar
def topbar(m, active, show_search=None):
    # The search box is only shown on the games grid itself (hidden on detail).
    if show_search is None:
        show_search = (active == "Games")
    m.rect(0, 0, W, 48, fill="topbar")
    # logo
    m.rect(14, 13, 22, 22, fill="accent", r=4)
    m.rect(19, 18, 5, 12, fill="dark")
    m.rect(26, 18, 5, 12, fill="dark")
    m.text(44, 24, "SplitPlay", BOLD(16), "text", anchor="lm")
    # search (only on games)
    if show_search:
        m.rect(150, 9, 240, 30, fill="surface", outline="border", r=6)
        m.text(160, 24, "Search games", REG(13), "text2", anchor="lm")
    # nav centered
    items = ["Games", "Controls", "Settings"]
    f = BOLD(15)
    gap = 28
    widths = [m.textlen(t, f) for t in items]
    total = sum(widths) + gap * (len(items) - 1)
    x = W/2 - total/2
    for t, wd in zip(items, widths):
        col = "text" if t == active else "text2"
        m.text(x, 24, t, f, col, anchor="lm")
        if t == active:
            m.rect(x, 44, wd, 3, fill="accent", r=2)
        x += wd + gap
    # window buttons
    bx = W - 44 * 3
    for i in range(3):
        cx = bx + i * 44
        if i == 0:  # minimize
            m.line(cx + 17, 16, cx + 27, 16, "text2", 1)
        elif i == 1:  # maximize
            m.rect(cx + 17, 11, 10, 10, outline="text2", width=1)
        else:  # close
            m.line(cx + 17, 11, cx + 27, 21, "text2", 1)
            m.line(cx + 27, 11, cx + 17, 21, "text2", 1)

# ---------------------------------------------------------------- games page
GAMES = [
    ("Alien: Isolation", "#2A3A33"), ("Astroneer", "#243447"), ("Battlerite", "#3a2233"),
    ("Counter-Strike 2", "#3a3320"), ("Dead by Daylight", "#202327"), ("Overwatch", "#23394a"),
    ("Path of Exile", "#3a2420"), ("Project Cars 2", "#203a3a"), ("ROBLOX", "#2a2a2a"),
    ("Rocket League", "#1f3350"),
]
def games_page():
    m = M()
    topbar(m, "Games")
    m.text(33, 84, "My Games", BOLD(26), "text", anchor="lm")
    tw, th, mg = 178, 250, 16
    x0, y0 = 33, 108
    cols = 5
    for i, (name, base) in enumerate(GAMES):
        r, c = divmod(i, cols)
        x = x0 + c * (tw + mg)
        y = y0 + r * (th + mg)
        m.vgrad(x, y, tw, th, base, "#14181d", r=8)
        # bottom scrim + title (capsule style)
        m.rect(x, y + th - 64, tw, 64, fill=None)
        m.text(x + tw/2, y + th - 30, name, BOLD(15), "text", anchor="mm")
        if i == 5:  # hovered tile -> accent border
            m.rect(x-1, y-1, tw+2, th+2, outline="accent", width=2, r=9)
    m.save("01-games.png")

# ---------------------------------------------------------------- detail page
def detail_page():
    m = M()
    topbar(m, "Games", show_search=False)
    # header hero
    m.vgrad(1, 48, W-2, 200, "#26465a", "#1B2027")
    m.rect(24, 66, 150, 34, fill="surfaceAlt", outline="border", r=6)
    m.text(99, 83, "‹  Library", REG(14), "text2", anchor="mm")
    m.text(24, 220, "Overwatch", BOLD(34), "text", anchor="lm")

    lx = 33
    ty = 280
    label = BOLD(13)
    # SCREEN SPLIT
    m.text(lx, ty, "SCREEN SPLIT", label, "text2", anchor="lm")
    m.rect(lx, ty+14, 200, 40, fill="accent", r=6)
    m.text(lx+100, ty+34, "Vertical (left / right)", REG(13), "dark", anchor="mm")
    m.rect(lx+210, ty+14, 220, 40, fill="surfaceAlt", outline="border", r=6)
    m.text(lx+320, ty+34, "Horizontal (top / bottom)", REG(13), "text2", anchor="mm")
    # DISPLAY
    dy = ty + 78
    m.text(lx, dy, "DISPLAY", label, "text2", anchor="lm")
    m.rect(lx, dy+14, 320, 34, fill="surface", outline="border", r=4)
    m.text(lx+12, dy+31, "Primary 1920x1080", REG(13), "text", anchor="lm")
    m.text(lx+300, dy+31, "▾", REG(13), "text2", anchor="mm")
    # CONTROLLER ASSIGNMENT
    cy = dy + 70
    m.text(lx, cy, "CONTROLLER ASSIGNMENT", label, "text2", anchor="lm")
    for i in range(2):
        ry = cy + 14 + i * 56
        m.rect(lx, ry, 420, 46, fill="surface", r=6)
        m.text(lx+16, ry+23, f"Player {i+1}", BOLD(15), "text", anchor="lm")
        m.rect(lx+186, ry+8, 220, 30, fill="surfaceAlt", outline="border", r=4)
        m.text(lx+198, ry+23, f"Controller {i+1}", REG(13), "text", anchor="lm")
        m.text(lx+390, ry+23, "▾", REG(12), "text2", anchor="mm")
    hint_y = cy + 14 + 2 * 56 + 8
    m.text(lx, hint_y, "Each player needs a different controller. Keyboard/mouse is not", REG(12), "text2", anchor="lm")
    m.text(lx, hint_y+16, "supported in split-screen.", REG(12), "text2", anchor="lm")

    # right column: preview + options + start
    rx = 760
    m.text(rx, ty, "PREVIEW", label, "text2", anchor="lm")
    m.rect(rx, ty+14, 380, 220, fill="#11161B", outline="border", r=8)
    pv_x, pv_y, pv_w, pv_h = rx+12, ty+26, 356, 196
    # vertical split preview
    half = pv_w // 2
    m.rect(pv_x, pv_y, half-3, pv_h, fill="accent", r=4)
    m.rect(pv_x+half+3, pv_y, half-3, pv_h, fill="surfaceAlt", r=4)
    cx1 = pv_x + (half-3)/2
    cx2 = pv_x + half + 3 + (half-3)/2
    cyc = pv_y + pv_h/2
    m.text(cx1, cyc-16, "Player 1", BOLD(15), "dark", anchor="mm")
    m.text(cx1, cyc+4, "Controller 1", REG(11), "dark", anchor="mm")
    m.text(cx1, cyc+22, "960 × 1080", MONO(10), "dark", anchor="mm")
    m.text(cx2, cyc-16, "Player 2", BOLD(15), "text", anchor="mm")
    m.text(cx2, cyc+4, "Controller 2", REG(11), "text2", anchor="mm")
    m.text(cx2, cyc+22, "960 × 1080", MONO(10), "text2", anchor="mm")

    oy = ty + 250
    # checkboxes
    m.rect(rx, oy, 16, 16, fill="accent", r=3)
    m.line(rx+4, oy+8, rx+7, oy+12, "dark", 2); m.line(rx+7, oy+12, rx+13, oy+4, "dark", 2)
    m.text(rx+24, oy+8, "Isolate controllers (one pad per window)", REG(13), "text2", anchor="lm")
    m.rect(rx, oy+26, 16, 16, outline="border", width=1, r=3)
    m.text(rx+24, oy+34, "Test mode (placeholder windows, don't launch game)", REG(13), "text2", anchor="lm")
    # start button
    by = oy + 60
    m.rect(rx, by, 380, 44, fill="accent", r=6)
    m.text(rx+190, by+22, "Start Split-Screen", BOLD(15), "dark", anchor="mm")
    # status
    m.text(rx, by+62, 'Launched "Overwatch" as a vertical split.', REG(12), "text2", anchor="lm")
    m.text(rx, by+78, "Controller input is isolated per window.", REG(12), "accent", anchor="lm")
    m.save("02-detail.png")

# ---------------------------------------------------------------- controls page
def controls_page():
    m = M()
    topbar(m, "Controls")
    m.text(33, 84, "Controls", BOLD(26), "text", anchor="lm")
    for i in range(2):
        y = 120 + i * 70
        m.rect(33, y, 420, 58, fill="surface", r=8)
        # round badge with a small drawn gamepad icon (no emoji font dependency)
        m.rect(49, y+10, 38, 38, fill="accent", r=19)
        gx, gy = 68, y+29
        m.rect(gx-9, gy-4, 18, 9, fill="dark", r=4)        # pad body
        m.d.ellipse([(gx-12)*S, (gy-3)*S, (gx-5)*S, (gy+4)*S], fill=hx("dark"))   # left grip
        m.d.ellipse([(gx+5)*S, (gy-3)*S, (gx+12)*S, (gy+4)*S], fill=hx("dark"))   # right grip
        m.line(gx-7, gy, gx-3, gy, "accent", 1)            # d-pad h
        m.line(gx-5, gy-2, gx-5, gy+2, "accent", 1)        # d-pad v
        m.d.ellipse([(gx+3)*S, (gy-2)*S, (gx+6)*S, (gy+1)*S], fill=hx("accent"))  # button
        m.text(101, y+22, f"Controller {i+1}", BOLD(15), "text", anchor="lm")
        m.text(101, y+40, "XInput - Connected", REG(12), "text2", anchor="lm")
        m.rect(323, y+16, 110, 26, fill="surfaceAlt", outline="border", r=6)
        m.text(378, y+29, "Test (rumble)", REG(12), "text2", anchor="mm")
    m.save("03-controls.png")

games_page()
detail_page()
controls_page()
