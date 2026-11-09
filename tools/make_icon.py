"""Build the default icon, or combine two local brand images for a personal build."""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def brand_mark(path, is_white):
    image = Image.open(path).convert("RGBA")
    mask = Image.new("L", image.size)
    if is_white:
        opacity = (min(a, max(0, min(255, round((min(r, g, b) - 165) * 255 / 84))))
                   for r, g, b, a in image.getdata())
        image = Image.new("RGBA", image.size, "#ffffff")
    else:
        opacity = (min(a, max(0, min(255, round((230 - min(r, g, b)) * 255 / 65))))
                   for r, g, b, a in image.getdata())
    mask.putdata(list(opacity))
    image.putalpha(mask)
    return image.crop(mask.getbbox())


def place(canvas, mark, x, y, height):
    width = round(height * mark.width / mark.height)
    scaled = mark.resize((width, height), Image.Resampling.LANCZOS)
    canvas.alpha_composite(scaled, (round(x - width / 2), round(y - height / 2)))


def draw_default(d):
    # Folder on the left, package on the right. Both are original vector shapes.
    dark = "#185747"
    d.rounded_rectangle((85, 145, 355, 318), radius=19, fill=dark)
    d.rounded_rectangle((85, 115, 219, 178), radius=19, fill=dark)
    d.polygon([(110, 188), (376, 188), (344, 311), (100, 311)], fill="#3f927d")
    d.line([(455, 520), (558, 459), (661, 520), (661, 630), (558, 691), (455, 630), (455, 520)],
           fill="#ffffff", width=20, joint="curve")
    d.line([(455, 520), (558, 581), (661, 520)], fill="#ffffff", width=20, joint="curve")
    d.line([(558, 581), (558, 691)], fill="#ffffff", width=20)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--orca-logo", type=Path)
    parser.add_argument("--bambu-logo", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if bool(args.orca_logo) != bool(args.bambu_logo):
        parser.error("Provide both local logo files, or omit both for the default icon.")

    size = 768
    image = Image.new("RGBA", (size, size), "#f4f8fa")
    d = ImageDraw.Draw(image)
    d.polygon([(size - 1, 0), (size - 1, size - 1), (0, size - 1)], fill="#00ac41")
    d.line([(0, size - 1), (size - 1, 0)], fill="#185747", width=round(0.028 * size))
    if args.orca_logo:
        place(image, brand_mark(args.orca_logo, is_white=False), 0.294 * size, 0.280 * size, round(0.441 * size))
        place(image, brand_mark(args.bambu_logo, is_white=True), 0.728 * size, 0.711 * size, round(0.436 * size))
    else:
        draw_default(d)
    edge = Image.new("L", image.size)
    ImageDraw.Draw(edge).rounded_rectangle((4, 4, size - 5, size - 5), radius=round(0.173 * size), fill=255)
    image.putalpha(edge)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    image.save(args.output, sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])


if __name__ == "__main__":
    main()
