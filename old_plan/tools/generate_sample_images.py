from __future__ import annotations

import argparse
import struct
from pathlib import Path


def write_bmp(path: Path, width: int, height: int, *, rgb=(255, 255, 255), vertical_line: bool = False) -> None:
    r, g, b = rgb
    row_bytes = width * 3
    padding = (4 - (row_bytes % 4)) % 4
    stride = row_bytes + padding
    image_size = stride * height

    # BITMAPFILEHEADER (14 bytes) + BITMAPINFOHEADER (40 bytes)
    file_header_size = 14
    info_header_size = 40
    pixel_offset = file_header_size + info_header_size
    file_size = pixel_offset + image_size

    bf = struct.pack("<2sIHHI", b"BM", file_size, 0, 0, pixel_offset)
    bi = struct.pack(
        "<IIIHHIIIIII",
        info_header_size,
        width,
        height,  # bottom-up
        1,  # planes
        24,  # bpp
        0,  # BI_RGB
        image_size,
        2835,  # 72 DPI
        2835,  # 72 DPI
        0,
        0,
    )

    # Pixel data: BGR, bottom-up
    pad = b"\x00" * padding
    line_x = width // 2

    with path.open("wb") as f:
        f.write(bf)
        f.write(bi)
        for y in range(height):
            row = bytearray()
            for x in range(width):
                rr, gg, bb = r, g, b
                if vertical_line and x == line_x:
                    rr, gg, bb = 80, 80, 80
                row.extend((bb, gg, rr))
            f.write(row)
            if padding:
                f.write(pad)


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate sample BMP images for KenpinTool.Prototype.")
    parser.add_argument("out_dir", type=Path, help="Output directory")
    parser.add_argument("--width", type=int, default=900)
    parser.add_argument("--height", type=int, default=1200)
    args = parser.parse_args()

    out_dir: Path = args.out_dir
    out_dir.mkdir(parents=True, exist_ok=True)

    samples = [
        ("001_OK.bmp", (40, 167, 69), False),  # green-ish
        ("002_STR-01S.bmp", (255, 193, 7), False),
        ("003_STR-03S.bmp", (255, 193, 7), False),
        ("004_STR-02.bmp", (220, 53, 69), False),
        ("005_STR-04.bmp", (253, 126, 20), False),
        ("006_QLT-05.bmp", (245, 245, 245), True),  # vertical line
        ("007_OCR-01.bmp", (253, 126, 20), False),
        ("008_DWG-02.bmp", (253, 126, 20), False),
        ("009_STR-01S_QLT-04.bmp", (255, 193, 7), False),
    ]

    for name, rgb, vline in samples:
        write_bmp(out_dir / name, args.width, args.height, rgb=rgb, vertical_line=vline)

    print(f"Generated {len(samples)} images in: {out_dir.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

