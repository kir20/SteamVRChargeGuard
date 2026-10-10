"""Draws assets/icon.ico: a shield with a lightning bolt (charge guard)."""
import os
from PIL import Image, ImageDraw

S = 1024
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((40, 40, S - 40, S - 40), radius=210, fill=(23, 37, 84, 255))
shield = [(512, 150), (812, 260), (812, 520), (770, 680), (650, 800), (512, 880),
          (374, 800), (254, 680), (212, 520), (212, 260)]
d.polygon(shield, fill=(56, 189, 248, 255))
bolt = [(560, 250), (360, 560), (500, 560), (450, 790), (670, 450), (525, 450), (590, 250)]
d.polygon(bolt, fill=(250, 204, 21, 255), outline=(23, 37, 84, 255), width=18)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'icon.ico')
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(out.replace('.ico', '.png'))
print('wrote', out)
