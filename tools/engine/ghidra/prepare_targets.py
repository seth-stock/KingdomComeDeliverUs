"""Make bounded Ghidra input from the locally installed PE's unwind table.

Output goes to ignored _work, not a distributable source/payload directory.
"""
from pathlib import Path
import struct,sys,bisect
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

binary=Path(sys.argv[1]);out=Path(sys.argv[2]);seeds=sys.argv[3:]
d=binary.read_bytes();pe=struct.unpack_from('<I',d,0x3c)[0]
n=struct.unpack_from('<H',d,pe+6)[0];opt=pe+24
base=struct.unpack_from('<Q',d,opt+24)[0];sh=opt+struct.unpack_from('<H',d,pe+20)[0]
sections=[];functions=[]
for i in range(n):
    at=sh+i*40;name=d[at:at+8].rstrip(b'\0')
    _,rva,size,offset=struct.unpack_from('<IIII',d,at+8)
    sections.append((rva,size,offset))
    if name==b'.pdata':
        for p in range(offset,offset+size-11,12):
            start,end,_=struct.unpack_from('<III',d,p)
            if start and end>start:functions.append((base+start,base+end))
functions.sort();begins=[f[0] for f in functions]
def containing(va):
    i=bisect.bisect_right(begins,va)-1
    if i>=0 and functions[i][0]<=va<functions[i][1]:return functions[i]
    # Leaf functions may have no unwind row. End at their terminal instead
    # of assigning adjacent helpers to a fictitious 64-byte function body.
    for ins in decoder.disasm(data(va,va+64),va):
        if ins.mnemonic.startswith('ret') or ins.mnemonic=='jmp':return va,ins.address+ins.size
    return va,va+64
def data(va,end):
    for rva,size,offset in sections:
        if base+rva<=va<base+rva+size:
            at=offset+va-base-rva;return d[at:at+end-va]
    return b''
decoder=Cs(CS_ARCH_X86,CS_MODE_64)
selected={}
for seed in seeds:
    name,address=seed.split('=',1);start,end=containing(int(address,16))
    if end-start>20000:raise ValueError('Seed exceeds byte budget')
    selected[start]=(name,end)
# One level of direct callees only, bounded at 80. No whole-program analysis.
for start,(_,end) in list(selected.items()):
    for ins in decoder.disasm(data(start,end),start):
        if ins.mnemonic!='call' or not ins.op_str.startswith('0x'):continue
        target=int(ins.op_str,16)
        if not data(target,target+1):continue
        begin,stop=containing(target)
        if stop-begin<=20000 and len(selected)<80:selected.setdefault(begin,(f'callee_{begin:x}',stop))
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(''.join(f'{name}\t{start:x}\t{end-1:x}\n' for start,(name,end) in selected.items()),encoding='ascii')
print('Prepared',len(selected),'bounded functions',out)
