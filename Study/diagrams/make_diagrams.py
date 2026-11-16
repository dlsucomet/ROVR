# Study/diagrams/make_diagrams.py
# The two control diagrams for the tutorial card, one per condition, as SVG (no title row; the card has its own):
#   python make_diagrams.py <output folder>   -> rovr-diagram.svg, controller-diagram.svg
# Styled like the in-game UI (PanelBuilder.cs: dark glass, rounded, white / soft grey text) and laid out
# like a game's control screen: the controller in the middle, short labels on leader lines.
# Controllers drawn after the Meta Quest 3 Touch Plus: white handle, round dark face, stick on the outer
# side, X/Y (A/B) diagonally inward, Menu (left) / Meta (right) at the lower outer edge, grip on the inner
# side of the handle, trigger under the front of the face. 2:1, like the card's diagram frame (560 x 270).
import os
import sys
from xml.sax.saxutils import escape

W, H = 1600, 800
GLASS = "#121418"        # PanelBuilder Glass (0.07, 0.08, 0.10)
BUTTON = "#333a47"       # PanelBuilder ButtonFill (0.20, 0.23, 0.28)
SOFT = "#c7cfdb"         # PanelBuilder SoftText (0.78, 0.81, 0.86)
FAINT = "#ffffff"        # used at 12% like PanelBuilder Faint
LIT = "#f2f4f7"          # a control that does something
FONT = "Arial, Helvetica, sans-serif"  # Unity's LegacyRuntime font is Arial


def t(x, y, s, size=20, weight=400, fill="#fff", anchor="start", opacity=1):
    a = f' text-anchor="{anchor}"' if anchor != "start" else ""
    w = f' font-weight="{weight}"' if weight != 400 else ""
    o = f' fill-opacity="{opacity}"' if opacity != 1 else ""
    return f'<text x="{x}" y="{y}" font-size="{size}"{w} fill="{fill}"{a}{o}>{escape(s)}</text>'


def leader(points):
    d = "M " + " L ".join(f"{x:.1f} {y:.1f}" for x, y in points)
    x, y = points[-1]
    return (f'<path d="{d}" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>'
            f'<circle cx="{x:.1f}" cy="{y:.1f}" r="5" fill="#fff"/>')


DEFS = """<defs>
  <linearGradient id="shell" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#ffffff"/><stop offset=".55" stop-color="#eceef1"/><stop offset="1" stop-color="#c9cdd3"/></linearGradient>
  <linearGradient id="shellv" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#f4f5f7"/><stop offset="1" stop-color="#c9cdd3"/></linearGradient>
  <radialGradient id="disc" cx=".42" cy=".35" r=".75"><stop offset="0" stop-color="#4b5058"/><stop offset="1" stop-color="#25282d"/></radialGradient>
  <radialGradient id="cap" cx=".4" cy=".35" r=".7"><stop offset="0" stop-color="#3a3f46"/><stop offset="1" stop-color="#16181b"/></radialGradient>
  <radialGradient id="glow" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#ffffff" stop-opacity=".35"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></radialGradient>
  <filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="6" stdDeviation="8" flood-color="#000" flood-opacity=".5"/></filter>
</defs>"""

# ---------------------------------------------------------------- controller, top view
# Local coordinates are for the LEFT controller; the right one is its mirror image.
POS = {"stick": (-50, -46), "low": (-6, 22), "up": (40, -30), "sys": (-70, 40)}
HANDLE = "M -100 40 C -114 140 -102 262 -80 350 C -71 388 -20 395 -9 362 C 8 300 30 180 54 60 Z"
GRIP = "M 34 110 C 58 104 70 122 66 184 C 64 202 46 206 34 192 Z"


def meta_logo(x, y, k, color, sw):
    p = (f"M {x} {y} C {x-3*k} {y-5*k} {x-9*k} {y-5*k} {x-9*k} {y} C {x-9*k} {y+5*k} {x-3*k} {y+5*k} {x} {y} "
         f"C {x+3*k} {y-5*k} {x+9*k} {y-5*k} {x+9*k} {y} C {x+9*k} {y+5*k} {x+3*k} {y+5*k} {x} {y} Z")
    return f'<path d="{p}" fill="none" stroke="{color}" stroke-width="{sw}" stroke-linejoin="round"/>'


def chevrons(x, y, s, dirs, color):
    out, a, c = [], 39 * s, 6 * s
    for dx, dy in dirs:
        tx, ty = x + dx * a, y + dy * a
        bx, by = x + dx * (a - 2 * c), y + dy * (a - 2 * c)
        px, py = -dy * c, dx * c
        out.append(f'<path d="M {bx+px:.1f} {by+py:.1f} L {tx:.1f} {ty:.1f} L {bx-px:.1f} {by-py:.1f}" fill="none" '
                   f'stroke="{color}" stroke-width="{3.5*s:.1f}" stroke-linecap="round" stroke-linejoin="round"/>')
    return out


def controller_top(cx, cy, s, side, lit, arrows=""):
    """lit: the controls that do something (stick / low / up / sys). arrows: '4' walk, '2' turn."""
    m = 1 if side == "L" else -1
    P = lambda x, y: (cx + s * m * x, cy + s * y)
    out = [f'<g filter="url(#soft)"><g transform="translate({cx} {cy}) scale({s*m} {s})">',
           f'<path d="{HANDLE}" fill="url(#shell)" stroke="#6f7680" stroke-width="2.5"/>',
           f'<path d="{GRIP}" fill="#dfe2e6" stroke="#6f7680" stroke-width="2.5"/>',
           '<ellipse cx="0" cy="10" rx="134" ry="121" fill="url(#shellv)" stroke="#6f7680" stroke-width="2.5"/>',
           '<ellipse cx="0" cy="0" rx="132" ry="119" fill="url(#disc)" stroke="#0c0d0f" stroke-width="3"/>',
           '<ellipse cx="0" cy="0" rx="124" ry="111" fill="none" stroke="#ffffff" stroke-opacity=".08" stroke-width="3"/>',
           "</g></g>"]

    x, y = P(*POS["stick"])
    if "stick" in lit:
        out.append(f'<circle cx="{x}" cy="{y}" r="{70*s}" fill="url(#glow)"/>')
    out.append(f'<circle cx="{x}" cy="{y}" r="{48*s}" fill="#121417" stroke="#000" stroke-width="2"/>')
    if "stick" in lit:
        out.append(f'<circle cx="{x}" cy="{y}" r="{30*s}" fill="{LIT}" stroke="#ffffff" stroke-width="2"/>')
        out.append(f'<circle cx="{x}" cy="{y}" r="{18*s}" fill="none" stroke="#b9c0ca" stroke-width="2"/>')
    else:
        out.append(f'<circle cx="{x}" cy="{y}" r="{30*s}" fill="url(#cap)" stroke="#000" stroke-width="2"/>')
        out.append(f'<circle cx="{x}" cy="{y}" r="{18*s}" fill="none" stroke="#4a5058" stroke-width="2"/>')
    if arrows:
        dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)] if arrows == "4" else [(-1, 0), (1, 0)]
        out += chevrons(x, y, s, dirs, "#ffffff")

    letters = {"low": "X" if side == "L" else "A", "up": "Y" if side == "L" else "B"}
    for k in ("low", "up"):
        x, y = P(*POS[k])
        if k in lit:
            out.append(f'<circle cx="{x}" cy="{y}" r="{44*s}" fill="url(#glow)"/>')
            out.append(f'<circle cx="{x}" cy="{y}" r="{22*s}" fill="{LIT}" stroke="#ffffff" stroke-width="2"/>')
            out.append(t(x, y + 8.5 * s, letters[k], 25 * s, 700, "#15181c", "middle"))
        else:
            out.append(f'<circle cx="{x}" cy="{y}" r="{20*s}" fill="#1c1f23" stroke="#000" stroke-width="2"/>')
            out.append(t(x, y + 7.5 * s, letters[k], 21 * s, 700, "#7d858f", "middle"))

    x, y = P(*POS["sys"])
    out.append(f'<circle cx="{x}" cy="{y}" r="{14*s}" fill="#1c1f23" stroke="#000" stroke-width="2"/>')
    if side == "L":
        out.append(f'<path d="M {x-6*s} {y-5*s} h {12*s} M {x-6*s} {y} h {12*s} M {x-6*s} {y+5*s} h {12*s}" stroke="#7d858f" stroke-width="{2.4*s}" stroke-linecap="round"/>')
    else:
        out.append(meta_logo(x, y, 0.85 * s, "#7d858f", 2.2 * s))
    return "\n".join(out), {k: P(*v) for k, v in POS.items()}


# ---------------------------------------------------------------- controller, side view
# Front (trigger) to the left; flip=True puts it on the right. Seen from the inner side.
SIDE_BODY = "M -80 -100 C -70 -60 -66 -20 -60 12 C -50 80 -36 140 -26 176 C -18 204 26 206 34 182 C 50 120 72 20 94 -100 Z"
SIDE_CAP_SHELL = "M -114 -116 C -116 -102 -70 -92 -6 -92 C 56 -92 104 -98 106 -114 L 104 -124 C 60 -116 -60 -116 -112 -124 Z"
SIDE_CAP_TOP = "M -114 -118 C -116 -142 -62 -160 -4 -160 C 56 -160 108 -146 106 -120 C 60 -112 -60 -112 -114 -118 Z"
SIDE_TRIGGER = "M -78 -96 C -116 -88 -126 -40 -104 -8 C -95 4 -78 2 -74 -12 C -78 -42 -72 -72 -64 -94 Z"
SIDE_GRIP = "M -30 -58 C -30 -76 28 -76 30 -58 L 22 52 C 20 70 -18 70 -20 52 Z"


def controller_side(cx, cy, s, flip=False):
    m = -1 if flip else 1
    out = [f'<g filter="url(#soft)" transform="translate({cx} {cy}) scale({s*m} {s})">',
           '<rect x="-16" y="-178" width="26" height="24" rx="5" fill="#16181b"/>',
           '<ellipse cx="-3" cy="-180" rx="22" ry="7" fill="#2b2f35" stroke="#000" stroke-width="2"/>',
           f'<path d="{SIDE_BODY}" fill="url(#shell)" stroke="#6f7680" stroke-width="2.5"/>',
           f'<path d="{SIDE_GRIP}" fill="#dfe2e6" stroke="#6f7680" stroke-width="2.5"/>',
           '<ellipse cx="-92" cy="-48" rx="40" ry="62" fill="url(#glow)"/>',
           f'<path d="{SIDE_TRIGGER}" fill="{LIT}" stroke="#ffffff" stroke-width="3"/>',
           f'<path d="{SIDE_CAP_SHELL}" fill="url(#shellv)" stroke="#6f7680" stroke-width="2.5"/>',
           f'<path d="{SIDE_CAP_TOP}" fill="url(#disc)" stroke="#0c0d0f" stroke-width="3"/>',
           "</g>"]
    return "\n".join(out), (cx - 104 * s * m, cy - 48 * s)


# ---------------------------------------------------------------- labels
def chip(x, y, glyph):
    """A button glyph like a game's control screen; (x, y) is its left edge, vertical centre."""
    if glyph == "TRIGGER":
        return (f'<rect x="{x}" y="{y-20}" width="112" height="40" rx="20" fill="{BUTTON}" stroke="#fff" stroke-opacity=".25" stroke-width="2"/>'
                + t(x + 56, y + 6, "TRIGGER", 16, 700, "#fff", "middle"), 112)
    if glyph == "MIC":
        return mic(x + 20, y, 20, False), 40
    return (f'<circle cx="{x+20}" cy="{y}" r="20" fill="{BUTTON}" stroke="#fff" stroke-opacity=".25" stroke-width="2"/>'
            + t(x + 20, y + 7, glyph, 20, 700, "#fff", "middle"), 40)


def mic(x, y, r, off):
    k = r / 24
    g = [f'<circle cx="{x}" cy="{y}" r="{r}" fill="{BUTTON}" stroke="#fff" stroke-opacity=".25" stroke-width="2"/>',
         f'<g fill="none" stroke="#fff" stroke-width="{2.6*k:.2f}" stroke-linecap="round">'
         f'<rect x="{x-5*k}" y="{y-13*k}" width="{10*k}" height="{16*k}" rx="{5*k}" fill="#fff"/>'
         f'<path d="M {x-9*k} {y-2*k} a {9*k} {9*k} 0 0 0 {18*k} 0"/>'
         f'<path d="M {x} {y+7*k} v {5*k} M {x-5*k} {y+12*k} h {10*k}"/></g>']
    if off:
        g.append(f'<path d="M {x-12*k} {y-13*k} L {x+12*k} {y+13*k}" stroke="{BUTTON}" stroke-width="{7*k:.2f}" stroke-linecap="round"/>')
        g.append(f'<path d="M {x-12*k} {y-13*k} L {x+12*k} {y+13*k}" stroke="#ff4d4d" stroke-width="{3.2*k:.2f}" stroke-linecap="round"/>')
    return "\n".join(g)


def frame(parts, dx=0, dy=0):
    bg = f'<rect x="1" y="1" width="{W-2}" height="{H-2}" rx="28" fill="{GLASS}" stroke="{FAINT}" stroke-opacity=".12" stroke-width="2"/>'
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" font-family="{FONT}">\n'
            + DEFS + "\n" + bg + f'\n<g transform="translate({dx} {dy})">\n' + "\n".join(parts) + "\n</g>\n</svg>\n")


# ================================================================ ROVR (voice)
def rovr():
    p = []
    # controller
    ctl, a = controller_top(760, 420, 0.85, "R", {"low", "up"})
    p.append(ctl)
    side, trig = controller_side(1040, 560, 0.62, flip=True)
    p.append(side)

    ax, ay = a["low"]
    p.append(t(1544, 260, "Mic on / off", 30, 700, "#fff", "end"))
    p.append(t(1544, 290, "Tap", 20, 400, SOFT, "end"))
    p.append(t(1544, 350, "Show / hide panel", 30, 700, "#fff", "end"))
    p.append(t(1544, 380, "Hold", 20, 400, SOFT, "end"))
    p.append(chip(1230 - 70, 304, "A")[0])
    p.append(f'<path d="M 1230 250 L 1214 250 L 1214 370 L 1230 370" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>')
    p.append(leader([(1158, 304), (1000, 304), (858, ay - 4), (ax + 16, ay - 3)]))

    bx, by = a["up"]
    # B above the A block: its line drops straight onto the button, clear of A's.
    p.append(t(1544, 150, "Cancel command", 30, 700, "#fff", "end"))
    p.append(t(1544, 180, "Tap", 20, 400, SOFT, "end"))
    p.append(chip(1160, 154, "B")[0])
    p.append(leader([(1158, 154), (bx, 154), (bx, by - 20)]))

    p.append(t(1544, 560, "Select", 30, 700, "#fff", "end"))
    p.append(t(1544, 590, "Point, then squeeze", 20, 400, SOFT, "end"))
    p.append(chip(1262, 550, "TRIGGER")[0])
    tx, ty = trig
    p.append(leader([(1252, 550), (1180, 550), (tx + 6, ty)]))
    return frame(p, -296, -70)


# ================================================================ Manual (controller)
def manual():
    p = []
    L, la = controller_top(500, 430, 0.85, "L", {"stick"}, arrows="4")
    R, ra = controller_top(1100, 430, 0.85, "R", {"stick", "low"}, arrows="2")
    p += [L, R]
    side, trig = controller_side(800, 520, 0.62)
    p.append(side)

    # left stick
    p.append(chip(56, 220, "L")[0])
    p.append(t(112, 230, "Walk", 30, 700))
    p.append(t(112, 260, "Push further = faster", 20, 400, SOFT))
    sx, sy = la["stick"]
    p.append(leader([(330, 220), (360, 220), (sx - 18, sy - 30)]))

    # right stick
    p.append(t(1544, 230, "Turn", 30, 700, "#fff", "end"))
    p.append(t(1544, 260, "Left / right", 20, 400, SOFT, "end"))
    p.append(chip(1544 - 86 - 56, 220, "R")[0])
    sx, sy = ra["stick"]
    p.append(leader([(1390, 220), (1250, 220), (sx + 18, sy - 30)]))

    # A
    ax, ay = ra["low"]
    p.append(t(1544, 450, "Close card", 30, 700, "#fff", "end"))
    p.append(t(1544, 480, "Tap", 20, 400, SOFT, "end"))
    p.append(t(1544, 540, "Show / hide panel", 30, 700, "#fff", "end"))
    p.append(t(1544, 570, "Hold", 20, 400, SOFT, "end"))
    p.append(f'<path d="M 1300 440 L 1284 440 L 1284 560 L 1300 560" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>')
    p.append(chip(1230, 500, "A")[0])
    p.append(leader([(1230, 500), (1180, 500), (ax + 10, ay + 14)]))

    # trigger
    tx, ty = trig
    p.append(chip(620, 210, "TRIGGER")[0])
    p.append(t(748, 220, "Select", 30, 700))
    p.append(t(748, 250, "Point, then squeeze", 20, 400, SOFT))
    p.append(leader([(676, 232), (676, 290), (tx - 4, ty)]))
    return frame(p, 0, -70)


out = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(out, "rovr-diagram.svg"), "w", encoding="utf-8") as f:
    f.write(rovr())
with open(os.path.join(out, "controller-diagram.svg"), "w", encoding="utf-8") as f:
    f.write(manual())
print("ok")
