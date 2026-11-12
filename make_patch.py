"""Create an installable delta from the original backup and the localized game."""
import argparse
import collections
import hashlib
from pathlib import Path
import struct
from zipfile import ZIP_DEFLATED, ZipFile

def digest(data):
    return hashlib.sha256(data).hexdigest()

def delta(before, after):
    # Copy unchanged spans from the user's installation and store changed bytes.
    step, block = 8, 16
    locations = collections.defaultdict(list)
    for i in range(0, len(before)-block+1, step):
        locations[before[i:i+block]].append(i)
    output = bytearray()
    literal, position, previous = 0, 0, 0
    while position+block <= len(after):
        candidates = locations.get(after[position:position+block], ())
        best, length = 0, 0
        for index in candidates:
            if index < previous-512 and len(candidates)>30:
                continue
            size = block
            while before[index+size:index+size+64] == after[position+size:position+size+64] and position+size+64 <= len(after):
                size += 64
            while index+size < len(before) and position+size < len(after) and before[index+size] == after[position+size]:
                size += 1
            if size > length:
                best, length = index, size
            if length >= 128:
                break
        if length < block:
            position += 1
            continue
        while best and position>literal and before[best-1] == after[position-1]:
            best -= 1
            position -= 1
            length += 1
        if position>literal:
            output += struct.pack('<BI',1,position-literal) + after[literal:position]
        output += struct.pack('<BII',0,best,length)
        position += length
        literal = position
        previous = best+length
    if literal<len(after):
        output += struct.pack('<BI',1,len(after)-literal) + after[literal:]
    return output

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('game',type=Path)
    parser.add_argument('backups',type=Path,nargs='+')
    args=parser.parse_args()
    originals={}
    for backup in args.backups:
        with ZipFile(backup) as archive:
            for name in archive.namelist():
                originals.setdefault(name,archive.read(name))
    plugin='www/js/plugins/HiraganaItaliano.js'
    lines=[]
    with ZipFile(Path(__file__).with_name('patch.zip'),'w',ZIP_DEFLATED,compresslevel=9) as archive:
        for name,before in sorted([*originals.items(),(plugin,None)]):
            assert name.startswith('www/') and '..' not in Path(name).parts
            after=(args.game/name).read_bytes()
            if before == after:
                continue
            item=str(len(lines))
            archive.writestr(item,delta(before or b'',after))
            lines.append('\t'.join([name,digest(before) if before is not None else '-',
                                    digest(after),str(len(after)),item]))
        archive.writestr('manifest.tsv','\n'.join(lines)+'\n')
    print(f'{len(lines)} file nella patch.')
if __name__=='__main__':
    main()

