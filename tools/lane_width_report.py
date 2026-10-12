import json, os, numpy as np
from scipy import ndimage as ndi
from PIL import Image, ImageDraw
t=os.environ.get("TEMP",".")
raw=open('src/Persistence/Initialization/Resources/Terrain201.att','rb').read()[3:]
m=np.frombuffer(raw,dtype=np.uint8).reshape(256,256)
blocked=(m==4)
lab,n=ndi.label(blocked)
border=set(lab[0,:])|set(lab[-1,:])|set(lab[:,0])|set(lab[:,-1]); border.discard(0)
ext=np.isin(lab,list(border))&blocked
wl,k=ndi.label(~blocked); sz=ndi.sum(~blocked,wl,range(1,k+1)); main=wl==1+int(np.argmax(sz))
play=main
dist=ndi.distance_transform_edt(~blocked)   # distance to nearest wall cell (any wall)
width=2*dist
d=json.load(open('src/GameLogic/PlugIns/Moba/MobaLayout.json'))
def interp(wps,step=1.0):
    pts=[]
    for (x0,y0),(x1,y1) in zip(wps[:-1],wps[1:]):
        L=max(1,int(np.hypot(x1-x0,y1-y0)/step))
        for i in range(L): pts.append((x0+(x1-x0)*i/L,y0+(y1-y0)*i/L))
    pts.append(tuple(wps[-1])); return pts
print('anchos medidos = 2 x distancia a la pared mas cercana (celdas), a lo largo de cada carril')
for name,w in d['lanes'].items():
    pts=interp(w)
    ws=np.array([width[int(round(y)),int(round(x))] for x,y in pts])
    print(f'{name:4s} largo={len(pts):4d} celdas  ancho min={ws.min():5.1f}  p10={np.percentile(ws,10):5.1f} mediana={np.median(ws):5.1f} p90={np.percentile(ws,90):5.1f} max={ws.max():5.1f}')
# open fields: playable cells whose nearest wall is >= R
for R in (8,10,12,15):
    f=(dist>=R)&play
    l2,c=ndi.label(f)
    big=[(int((l2==i).sum()),ndi.center_of_mass(l2==i)) for i in range(1,c+1)]
    big=[b for b in big if b[0]>=20]
    print(f'campos con distancia a pared >= {R} (ancho >= {2*R}): {len(big)} zonas, area total {sum(b[0] for b in big)} celdas')
f=(dist>=10)&play
l2,c=ndi.label(f)
print('zonas >= ancho 20 (y, x del centro, area):')
for i in range(1,c+1):
    a=int((l2==i).sum())
    if a>=25:
        cy,cx=ndi.center_of_mass(l2==i); print(f'  centro x={cx:5.0f} y={cy:5.0f} area={a:5d} maxancho={2*dist[l2==i].max():4.1f}')
# image: width classes
cols={0:(40,40,40)}
im=Image.new('RGB',(256,256)); px=im.load()
for y in range(256):
    for x in range(256):
        if blocked[y,x]: c=(40,40,40)
        elif not main[y,x]: c=(90,70,70)
        else:
            w=width[y,x]
            c=(90,170,230) if w<8 else (110,200,120) if w<16 else (240,220,90) if w<24 else (240,150,60) if w<32 else (220,70,60)
        px[x,255-y]=c
im=im.resize((768,768),Image.NEAREST); dr=ImageDraw.Draw(im)
for name,w in d['lanes'].items():
    P=[(x*3+1,(255-y)*3+1) for x,y in w]; dr.line(P,fill=(255,255,255),width=1)
im.save('lane_width_map.png')
