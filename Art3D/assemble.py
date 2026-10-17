import numpy as np
from PIL import Image, ImageFilter
R='/Users/nadimbenhassen/My project (2)/Assets/Resources/Companion/'
fins=['cuivre','nuit','braise','prototype','dore']

def glow(im, src_im):
    """A soft halo round what shines (eye, bulb, flame, flash) - from the render where only they show."""
    a = np.asarray(im, np.float32) / 255.0
    rgb, al = a[..., :3], a[..., 3]
    s = np.asarray(src_im, np.float32) / 255.0
    src = Image.fromarray((np.dstack([s[..., :3] * s[..., 3:4], s[..., 3]]) * 255).astype(np.uint8), 'RGBA')
    halo = np.asarray(src.filter(ImageFilter.GaussianBlur(16)), np.float32) / 255.0
    halo2 = np.asarray(src.filter(ImageFilter.GaussianBlur(6)), np.float32) / 255.0
    ha = np.clip(halo[..., 3] * 1.7 + halo2[..., 3] * 0.9, 0, 1)
    hc = np.clip((halo[..., :3] * 1.7 + halo2[..., :3] * 0.9) / np.maximum(ha[..., None], 1e-4), 0, 1)
    k = 0.8
    out_a = al + ha * k * (1 - al)
    out_rgb = (rgb * al[..., None] + hc * (ha * k * (1 - al))[..., None]) / np.maximum(out_a[..., None], 1e-4)
    # a little of the light spills onto the robot itself
    out_rgb = np.clip(out_rgb + hc * (halo2[..., 3] * 0.3 * al)[..., None], 0, 1)
    return Image.fromarray((np.dstack([out_rgb, out_a]) * 255).astype(np.uint8), 'RGBA')

prev = Image.new('RGBA', (9 * 128, len(fins) * 256), (236, 150, 110, 255))
for r, f in enumerate(fins):
    old = Image.open(R + f'etienne_{f}.png').convert('RGBA') if True else None
    sheet = Image.new('RGBA', (256 * 9, 256), (0, 0, 0, 0))
    for i in range(9):
        im = glow(Image.open(f'out/{f}_{i}.png').convert('RGBA'), Image.open(f'out/{f}_{i}_glow.png').convert('RGBA'))
        im = im.resize((256, 256), Image.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1, percent=60, threshold=1))
        sheet.paste(im, (i * 256, 0))
    sheet.save(f'sheets/etienne_{f}.png', optimize=True)
    prev.alpha_composite(old.resize((9 * 128, 128)), (0, r * 256))
    prev.alpha_composite(sheet.resize((9 * 128, 128)), (0, r * 256 + 128))
prev.save('compare.png')
big = Image.new('RGBA', (1024, 512), (236, 150, 110, 255))
big.alpha_composite(glow(Image.open('out/cuivre_0.png').convert('RGBA'), Image.open('out/cuivre_0_glow.png').convert('RGBA')), (0, 0))
big.alpha_composite(glow(Image.open('out/nuit_8.png').convert('RGBA'), Image.open('out/nuit_8_glow.png').convert('RGBA')), (512, 0))
big.save('big.png')
print('ok')
