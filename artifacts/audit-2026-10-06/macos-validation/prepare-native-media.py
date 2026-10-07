import hashlib
import json
import os
import math
import pathlib
import struct
import subprocess
import wave
import zlib

simulator = os.environ['NOVALIST_AUDIT_SIMULATOR']
container = pathlib.Path(subprocess.check_output(['xcrun','simctl','get_app_container',simulator,'com.novalist.app','data'],text=True).strip())
roots = [container/'Documents'/'Audit Media A', container/'Documents'/'Audit Media B']

def png(width, height, rgb):
    def chunk(kind, data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data))
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))+chunk(b'IDAT',zlib.compress((b'\0'+bytes(rgb)*width)*height))+chunk(b'IEND',b'')

for index, root in enumerate(roots):
    media = root/'media'
    media.mkdir(parents=True,exist_ok=True)
    (media/'shared.png').write_bytes(png(2+index,2+index,(255 if index==0 else 0,0,255 if index else 0)))
    (media/'second.png').write_bytes(png(4,4,(0,255,0)))
    (media/'oversized.png').write_bytes(png(2,2,(0,0,0))+bytes(16*1024*1024+1))
    with wave.open(str(media/'tone.wav'),'wb') as audio:
        audio.setnchannels(1);audio.setsampwidth(2);audio.setframerate(16000)
        audio.writeframes(b''.join(struct.pack('<h',int(3000*math.sin(i*math.tau*440/16000))) for i in range(16000)))
    stream=b'BT /F1 20 Tf 20 100 Td (AUDIT PDF FIXTURE) Tj ET'
    objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 1 >>',b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 250 150] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',b'<< /Length '+str(len(stream)).encode()+b' >>\nstream\n'+stream+b'\nendstream']
    pdf=b'%PDF-1.4\n';offsets=[0]
    for i,obj in enumerate(objects,1):offsets.append(len(pdf));pdf+=str(i).encode()+b' 0 obj\n'+obj+b'\nendobj\n'
    start=len(pdf);pdf+=b'xref\n0 6\n0000000000 65535 f \n'+b''.join(f'{x:010} 00000 n \n'.encode() for x in offsets[1:])+b'trailer << /Size 6 /Root 1 0 R >>\nstartxref\n'+str(start).encode()+b'\n%%EOF\n'
    (media/'fixture.pdf').write_bytes(pdf)
    (root/'references.json').write_text(json.dumps({'image':'media/shared.png','audio':'media/tone.wav','pdf':'media/fixture.pdf','oversized':'media/oversized.png'}))
manifest={str(index):{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in root.rglob('*') if p.is_file()} for index,root in enumerate(roots)}
(pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-roots.json').write_text(json.dumps([str(x) for x in roots]))
(pathlib.Path(os.environ['NOVALIST_AUDIT_STATE']) / 'media-before.json').write_text(json.dumps(manifest,indent=2))
print('Two synthetic media roots prepared; original hashes retained.')
