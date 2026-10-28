"""Offline embedded-image resize for prepare-unit-model.cjs (Pillow required).

GLB on stdin/stdout; rebuild buffer views without changing geometry bytes.
The caller has already checked the source hash and core-static format.
"""
import io
import json
import struct
import sys
from PIL import Image


def resize(data, limit):
    if limit not in (256, 512, 1024, 2048):
        raise ValueError("Texture limit must be 256, 512, 1024 or 2048")
    length = struct.unpack_from('<I', data, 12)[0]
    doc = json.loads(data[20:20 + length])
    binary = data[28 + length:]
    if len(doc['buffers']) != 1 or 'uri' in doc['buffers'][0]:
        raise ValueError('Resize requires one embedded buffer')
    replacements = {}
    for image in doc.get('images', []):
        view_id = image['bufferView']
        view = doc['bufferViews'][view_id]
        start = view.get('byteOffset', 0)
        with Image.open(io.BytesIO(binary[start:start + view['byteLength']])) as decoded:
            decoded.load()
            if max(decoded.size) <= limit:
                continue
            # Keep alpha; use lossless PNG after the sole resize operation.
            decoded = decoded.convert('RGBA' if 'A' in decoded.getbands() else 'RGB')
            ratio = limit / max(decoded.size)
            decoded = decoded.resize(tuple(max(1, round(n * ratio)) for n in decoded.size), Image.Resampling.LANCZOS)
            output = io.BytesIO()
            decoded.save(output, format='PNG', compress_level=9, optimize=False)
            replacements[view_id] = output.getvalue()
            image['mimeType'] = 'image/png'
    parts, offset = [], 0
    for index, view in enumerate(doc['bufferViews']):
        start = view.get('byteOffset', 0)
        payload = replacements.get(index, binary[start:start + view['byteLength']])
        view['byteOffset'], view['byteLength'] = offset, len(payload)
        padded = payload + bytes(-len(payload) % 4)
        parts.append(padded)
        offset += len(padded)
    doc['buffers'][0]['byteLength'] = offset
    doc['asset']['extras']['runtimeTextureLimit'] = limit
    encoded = json.dumps(doc, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    encoded += b' ' * (-len(encoded) % 4)
    return (struct.pack('<5I', 0x46546c67, 2, 28 + len(encoded) + offset, len(encoded), 0x4e4f534a)
            + encoded + struct.pack('<2I', offset, 0x004e4942) + b''.join(parts))


if __name__ == '__main__':
    sys.stdout.buffer.write(resize(sys.stdin.buffer.read(), int(sys.argv[1])))
