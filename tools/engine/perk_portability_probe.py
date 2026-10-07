"""Read local perk definitions against a cloned save; never extract/package game assets."""
from pathlib import Path
import sys, struct, zlib, uuid, zipfile, importlib.util
import xml.etree.ElementTree as ET

data=Path(sys.argv[2]).read_bytes();raw=bytearray();at=0
while at<len(data)-64:
    packed,length=struct.unpack_from('<ii',data,at);size=length if packed==-1 else packed
    if not 0<=length<=32768 or not 0<=size<=32768:raise ValueError('Save bounds differ')
    part=data[at+8:at+8+size];part=part if packed==-1 else zlib.decompress(part)
    if len(part)!=length:raise ValueError('Save length differs')
    raw.extend(part);at+=8+size
def children(start,end):
    result=[]
    while start<end:
        tag,length=struct.unpack_from('<HI',raw,start)
        if start+6+length>end:raise ValueError('TLV bounds differ')
        result.append((tag,start+6,start+6+length));start+=6+length
    return result
def one(nodes,tag):
    found=[n for n in nodes if n[0]==tag]
    if len(found)!=1:raise ValueError('Missing/duplicate field')
    return found[0]
fields=children(4,len(raw)-1)
for tag in (0x1f4,0x1f6,0x7317,0x3529):
    _,start,end=one(fields,tag)
    if tag!=0x3529:fields=children(start,end)
souls=children(start+4,end);hero=uuid.UUID('a075f5f3-014e-4fd8-9b02-2f8e06d3e031').bytes_le
_,start,end=next(n for n in souls if raw[n[1]:n[1]+16]==hero)
fields=children(start+16,end)
for tag in (0x12fc,0x092a,0x137e):
    _,start,end=one(fields,tag);fields=children(start,end)
guids=set()
for tag,start,end in fields:
    if tag not in (0x3d8,0x3d9):continue
    _,s,e=one(children(start,end),0x137e)
    _,s,e=one(children(s,e),0x1379)
    b=bytes(raw[s:e]);guids.add(str(uuid.UUID(bytes=b[:8][::-1]+b[8:][::-1])))
spec=importlib.util.spec_from_file_location('pak',Path(__file__).parents[1]/'Inspect-EngineCapabilities.py')
pak=importlib.util.module_from_spec(spec);spec.loader.exec_module(pak)
seen=set()
with zipfile.ZipFile(Path(sys.argv[1])/'Data'/'Tables.pak') as archive:
    for entry in archive.infolist():
        if '/rpg/' not in entry.filename.lower() or 'perk' not in entry.filename.lower() or not entry.filename.lower().endswith('.xml'):continue
        tree=ET.fromstring(pak.read_entry(archive,entry))
        for row in tree.iter('row'):
            if row.attrib.get('perk_id','').lower() in guids and row.attrib.get('perk_name') and row.attrib['perk_id'] not in seen:
                seen.add(row.attrib['perk_id'])
                print(row.attrib['perk_id'],{k:v for k,v in row.attrib.items() if any(t in k.lower() for t in ('name','abilit','invulner','time','duration','flag','hide'))})
