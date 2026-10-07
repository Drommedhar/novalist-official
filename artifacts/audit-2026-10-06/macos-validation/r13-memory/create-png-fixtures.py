"""Reproduce only the four synthetic PNGs; assert the measured fixture hashes."""
import hashlib, json, pathlib, random, struct, sys, zlib
metadata=json.loads(pathlib.Path(__file__).with_name('fixtures.json').read_text())
out=pathlib.Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
def chunk(kind,data):
    return struct.pack('!I',len(data))+kind+data+struct.pack('!I',zlib.crc32(kind+data)&0xffffffff)
for name,item in metadata['files'].items():
    width,height=item['width'],item['height'];rng=random.Random(item['seed'])
    pixels=b''.join(b'\0'+rng.randbytes(width*4) for _ in range(height))
    data=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('!IIBBBBB',width,height,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(pixels,1))+chunk(b'IEND',b'')
    assert len(data)==item['bytes'] and hashlib.sha256(data).hexdigest()==item['sha256']
    with (out/name).open('xb') as stream:stream.write(data)
