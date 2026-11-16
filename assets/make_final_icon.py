from PIL import Image
import os

OUT = os.path.dirname(__file__)
src = Image.open(os.path.join(OUT, "logo-option-1.png")).convert("RGBA")
src = src.resize((512, 512), Image.LANCZOS)
src.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "logo.png"))
src.save(
    os.path.join(OUT, "app.ico"),
    format="ICO",
    sizes=[(s, s) for s in [16, 24, 32, 48, 64, 128, 256]],
)

# Dark-mode counterpart: light tile with dark mark, preserving antialiasing.
dark = Image.new("RGBA", src.size, (0, 0, 0, 0))
sp = src.load()
dp = dark.load()
for y in range(src.height):
    for x in range(src.width):
        r, g, b, a = sp[x, y]
        if a == 0:
            continue
        t = (r + g + b) / (3.0 * 255.0)
        if t < 0.2: t = 0.0
        elif t > 0.8: t = 1.0
        else: t = (t - 0.2) / 0.6
        rr = int(245 * (1 - t) + 17 * t)
        gg = int(246 * (1 - t) + 19 * t)
        bb = int(248 * (1 - t) + 24 * t)
        dp[x, y] = (rr, gg, bb, a)

dark.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "logo-dark.png"))
dark.save(
    os.path.join(OUT, "app-dark.ico"),
    format="ICO",
    sizes=[(s, s) for s in [16, 24, 32, 48, 64, 128, 256]],
)
print("final icon written")
