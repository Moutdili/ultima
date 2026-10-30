import re,sys
src=open('/home/mio/v2-miroir2/Ultima.Mirror/src/Ultima.Mirror/ObjectPaths.cs',encoding='utf-8').read()
base='/home/mio/sts2/game/pck/src/Core/'
n=bad=0
for m in re.finditer(r'new\((\w+), (K\("(\w+)"\)|"([^"]+)"), (true|false), "([^"]+):(\d+)"', src):
    member = m.group(3) or m.group(4)
    name = re.sub(r'^<(\w+)>P$', r'\1', member)
    f, line = m.group(6), int(m.group(7))
    text = open(base+f,encoding='utf-8').read().split('\n')[line-1]
    ok = re.search(r'\b'+re.escape(name)+r'\b', text) is not None
    n+=1
    print(('OK   ' if ok else 'ÉCART'), f'{f}:{line}', name, '|', text.strip()[:110])
    bad += 0 if ok else 1
print(f'{n} vérifiés, {bad} écart(s)')
