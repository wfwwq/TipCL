import json
from fontTools.ttLib import TTFont
from fontTools import subset
import os,sys; HERE=os.path.dirname(os.path.abspath(__file__)); exec(open(os.path.join(HERE,"map.py")).read()); os.chdir(sys.argv[1] if len(sys.argv)>1 else ".")
R=json.load(open('FluentSystemIcons-Regular.json'));F=json.load(open('FluentSystemIcons-Filled.json'))
reg=TTFont('FluentSystemIcons-Regular.ttf'); fil=TTFont('FluentSystemIcons-Filled.ttf')
rc=reg.getBestCmap(); fc=fil.getBestCmap()
print('upem',reg['head'].unitsPerEm, fil['head'].unitsPerEm)
newmap={}
order=reg.getGlyphOrder()[:]
for k,(st,n) in M.items():
  d,font,cm,suf=(R,reg,rc,'regular') if st=='r' else (F,fil,fc,'filled')
  name=next(f"ic_fluent_{n}_{s}_{suf}" for s in (20,24,16,28,32,12) if f"ic_fluent_{n}_{s}_{suf}" in d)
  g=cm[d[name]]
  if st=='f':
    newg='filled_'+g
    if newg not in reg['glyf'].glyphs:
      glyph=fil['glyf'][g]; glyph.expand(fil['glyf'])
      order.append(newg); reg['glyf'].glyphs[newg]=glyph; reg['hmtx'].metrics[newg]=fil['hmtx'].metrics[g]
    g=newg
  newmap[int(k,16)]=g
reg.setGlyphOrder(order); reg['glyf'].glyphOrder=order
if 'post' in reg: reg['post'].formatType=2.0
for t in reg['cmap'].tables:
  if t.isUnicode(): t.cmap=dict(newmap)
reg['cmap'].tables=[t for t in reg['cmap'].tables if t.isUnicode() and t.format in (4,12)]
reg.save('tmp.ttf')
f=TTFont('tmp.ttf')
opts=subset.Options(); opts.name_IDs=['*']; opts.notdef_outline=True; opts.layout_features=[]; opts.glyph_names=False
s=subset.Subsetter(opts); s.populate(unicodes=list(newmap)); s.subset(f)
for rec in f['name'].names:
  if rec.nameID in (1,16): rec.string='TipCL Icons'
  elif rec.nameID==4: rec.string='TipCL Icons Regular'
  elif rec.nameID==6: rec.string='TipCLIcons-Regular'
  elif rec.nameID==3: rec.string='TipCL Icons; based on Fluent UI System Icons (MIT)'
# 与 Segoe MDL2 Assets 一致：字形占满 0..em 的方框，基线在底部，避免图标上下偏移
upem=f['head'].unitsPerEm
f['hhea'].ascent=upem; f['hhea'].descent=0; f['hhea'].lineGap=0
f['OS/2'].sTypoAscender=upem; f['OS/2'].sTypoDescender=0; f['OS/2'].sTypoLineGap=0
f['OS/2'].usWinAscent=upem; f['OS/2'].usWinDescent=0
f.save('TipCLIcons.ttf')
print(len(f.getBestCmap()),'glyphs mapped')
