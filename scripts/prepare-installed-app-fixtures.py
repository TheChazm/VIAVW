"""Small disposable archive, RTF documents and raw AVI for installed-app research."""
import argparse
from pathlib import Path
import struct
import zipfile


def prepare(output, media=True):
    output = output.resolve()
    project = Path(__file__).resolve().parents[1]
    if not output.is_relative_to(project / "artifacts"):
        raise ValueError("Fixture directory must be inside VIAVW artifacts")
    output.mkdir(exist_ok=False)
    with zipfile.ZipFile(output / "sample.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("greeting.txt", "VIAVW archive extraction verified.\n")
        archive.writestr("nested/details.txt", "Second disposable archive member.\n")
    for name, text in (("word-first", "Original disposable Word document.\\par Second line for selection testing."),
                       ("word-second", "Second disposable Word document.")):
        (output / (name + ".rtf")).write_text(r"{\rtf1\ansi\deff0 {\fonttbl {\f0 Calibri;}}\f0\fs24 " + text + "}", encoding="ascii")
    if not media:
        print(output)
        return
    def chunk(tag, data):
        return tag + struct.pack("<I", len(data)) + data + (b"\0" if len(data) % 2 else b"")
    width, height, count, fps = 160, 90, 120, 10
    size = width * height * 3
    avih = struct.pack("<14I", 1000000 // fps, size * fps, 0, 0x10, count, 0, 1, size, width, height, 0, 0, 0, 0)
    strh = struct.pack("<4s4sIHHIIIIIIIIhhhh", b"vids", b"DIB ", 0, 0, 0, 0, 1, fps, 0, count, size, 0xffffffff, 0, 0, 0, width, height)
    strf = struct.pack("<IiiHHIIiiII", 40, width, height, 1, 24, 0, size, 0, 0, 0, 0)
    header = chunk(b"LIST", b"hdrl" + chunk(b"avih", avih) + chunk(b"LIST", b"strl" + chunk(b"strh", strh) + chunk(b"strf", strf)))
    frames, index, offset = [], [], 4
    for frame in range(count):
        data = bytearray()
        for y in range(height):
            for x in range(width):
                color = ((0, 0, 220) if x < width//2 else (0, 200, 0)) if y < height//2 else ((220, 0, 0) if x < width//2 else (0, 200, 220))
                data.extend((255, 255, 255) if abs(x - frame % width) < 3 else color)
        packet = chunk(b"00db", bytes(data))
        frames.append(packet)
        index.append(struct.pack("<4sIII", b"00db", 0x10, offset, len(data)))
        offset += len(packet)
    (output / "color-motion.avi").write_bytes(chunk(b"RIFF", b"AVI " + header + chunk(b"LIST", b"movi" + b"".join(frames)) + chunk(b"idx1", b"".join(index))))
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--no-video", action="store_true")
    args = parser.parse_args()
    prepare(args.output, media=not args.no_video)
