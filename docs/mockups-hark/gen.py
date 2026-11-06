I = {
'spark':'<path d="M8 1.5v3M8 11.5v3M1.5 8h3M11.5 8h3M3.4 3.4l2 2M10.6 10.6l2 2M3.4 12.6l2-2M10.6 5.4l2-2"/>',
'moon':'<path d="M13 9.5A5.5 5.5 0 0 1 6.5 3a5.5 5.5 0 1 0 6.5 6.5z"/>',
'play':'<path d="M5 3.5v9l7-4.5z" fill="currentColor" stroke="none"/>',
'gear':'<path d="M2.5 4.5h7M12.5 4.5h1M2.5 11.5h2M7.5 11.5h6"/><circle cx="11" cy="4.5" r="1.5"/><circle cx="6" cy="11.5" r="1.5"/>',
'wifi':'<path d="M2 6.5a9 9 0 0 1 12 0M4.2 9a5.6 5.6 0 0 1 7.6 0M6.4 11.4a2.3 2.3 0 0 1 3.2 0"/><circle cx="8" cy="13.4" r=".6" fill="currentColor"/>',
'down':'<path d="M8 2.5v8M4.8 7.6 8 10.8l3.2-3.2M3 13.5h10"/>',
'search':'<circle cx="7" cy="7" r="4.3"/><path d="m10.3 10.3 3.2 3.2"/>',
'chev':'<path d="m4.5 6.5 3.5 3.5 3.5-3.5"/>',
'right':'<path d="m6 4 4 4-4 4"/>',
}
def ic(n,cls='i',w=16): return f'<svg class="{cls}" viewBox="0 0 16 16" width="{w}" height="{w}">{I[n]}</svg>'
def titlebar(active, closehot=False):
    tabs=''.join(f'<div class="tab{" on" if t==active else ""}">{t}</div>' for t in ['启动','实例库','下载','插件','设置'])
    return f'''<div class="tb"><div class="logo"><i>T</i>TipCL</div><div class="tabs">{tabs}</div><div class="sp"></div>
<div class="tbr"><div class="ib" title="AI">{ic('spark')}</div><div class="ib">{ic('moon')}</div><div class="av"><b>1</b>111</div><div class="sep"></div>
<div class="cap"><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></div>
<div class="cap"><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9" rx="1.5"/></svg></div>
<div class="cap x{' hot' if closehot else ''}"><svg viewBox="0 0 10 10"><path d="M.5.5l9 9M9.5.5l-9 9"/></svg></div></div></div>'''
def page(title,active,body,hot=False):
    return f'<!doctype html><meta charset=utf-8><link rel=stylesheet href=common.css><style>{title}</style><div class="win">{titlebar(active,hot)}<div class="body">{body}</div></div>'

# ---------- Launch
launch_css='''
.hero{flex:1;display:flex}
.l{padding:0 0 0 56px;display:flex;flex-direction:column;justify-content:center}
.k{font-size:12px;color:#7a7e82;display:flex;align-items:center;gap:6px}
.k .dot{width:6px;height:6px;border-radius:50%;background:#2f9e6a}
.t{font-size:26px;font-weight:700;margin:8px 0 4px;display:flex;align-items:center;gap:8px}
.t svg{width:18px;height:18px;color:#9a9ea2}
.m{font-size:12.5px;color:#7a7e82;margin-bottom:22px}
.row{display:flex;gap:8px}
.r{border-left:1px solid #ecedeb;background:#fbfbfa;padding:22px 16px}
.h{font-size:12px;color:#7a7e82;margin:0 0 8px 6px}
.it{display:flex;align-items:center;gap:10px;padding:8px 6px;border-radius:6px;font-size:13px}
.it.on{background:#eef6f1}
.it .b{width:28px;height:28px;border-radius:6px;background:#e9ebe8;display:grid;place-items:center;font-size:11px;color:#5d6166;font-weight:600}
.it small{display:block;color:#8a8e92;font-size:11.5px}
.foot{height:34px;display:flex;align-items:center;padding:0 56px;font-size:12px;color:#7a7e82;gap:6px;border-top:1px solid #efefed}
.foot svg{width:12px;height:12px}
'''
launch=f'''<div class="hero"><div class="l">
<div class="k"><span class="dot"></span>就绪 · E:\\.minecraft</div>
<div class="t">Fabric 1.20.1 {ic('chev')}</div>
<div class="m">Java 17 · 4 GB · 32 个模组</div>
<div class="row"><div class="btn pri">{ic('play')}启动游戏</div><div class="btn sq" title="启动配置">{ic('gear')}</div><div class="btn" style="height:40px">{ic('wifi')}联机</div></div>
</div></div>
<div class="foot">Minecraft 26.4 Snapshot 7 已发布 {ic('right','i',12)}</div>'''
open('launch.html','w').write(page(launch_css,'启动',launch))

# ---------- Download
dl_css='''
.sub{display:flex;align-items:center;gap:2px;padding:12px 20px 0}
.s{font-size:13px;padding:6px 11px;border-radius:6px;color:#5d6166}
.s.on{background:#e8f3ed;color:#23744f;font-weight:600}
.task{margin-left:auto;display:flex;align-items:center;gap:6px;font-size:12.5px;color:#3a3d40;padding:5px 10px;border-radius:6px;border:1px solid #e0e2df;background:#fff}
.task b{background:#2f8f63;color:#fff;font-size:10.5px;border-radius:8px;padding:0 6px;font-weight:600}
.bar{display:flex;gap:8px;padding:14px 20px 10px;align-items:center}
.in{flex:1;height:32px;border:1px solid #dfe1de;border-radius:6px;background:#fff;display:flex;align-items:center;gap:8px;padding:0 10px;color:#9a9ea2;font-size:13px}
.f{font-size:12.5px;padding:5px 10px;border-radius:6px;color:#5d6166}
.f.on{background:#fff;border:1px solid #dfe1de;color:#1d1f21}
.list{margin:0 20px;background:#fff;border:1px solid #ebecea;border-radius:8px;overflow:hidden}
.v{display:flex;align-items:center;padding:0 14px;height:44px;border-top:1px solid #f1f1ef;font-size:13px}
.v:first-child{border-top:0}
.v .n{width:120px;font-weight:600}.v .tg{font-size:11px;padding:1px 7px;border-radius:4px;background:#eef0ee;color:#5d6166;margin-right:auto}
.v .tg.new{background:#e8f3ed;color:#23744f}
.v .d{color:#8a8e92;font-size:12px;width:110px}
.v .a{font-size:12.5px;color:#2f8f63;display:flex;align-items:center;gap:5px}
'''
rows=[('26.4-snap7','快照','new','2026-11-05'),('1.21.10','正式版','new','2026-10-07'),('1.21.9','正式版','','2026-09-30'),('1.21.8','正式版','','2026-07-17'),('1.20.1','正式版','','2023-06-12'),('1.12.2','正式版','','2017-09-18')]
vr=''.join(f'<div class="v"><span class="n">{a}</span><span class="tg {c}">{b}</span><span class="d">{d}</span><span class="a">{ic("down","i",14)}安装</span></div>' for a,b,c,d in rows)
subs=''.join(f'<div class="s{" on" if s=="原版" else ""}">{s}</div>' for s in ['原版','Mod','整合包','数据包','资源包','光影','世界'])
dl=f'''<div class="sub">{subs}<div class="task">{ic('down','i',14)}任务<b>2</b></div></div>
<div class="bar"><div class="in">{ic('search','i',14)}搜索版本</div><div class="f on">正式版</div><div class="f">快照</div><div class="f">远古</div></div>
<div class="list">{vr}</div>'''
open('download.html','w').write(page(dl_css,'下载',dl))

# ---------- Settings
st_css='''
.wrap{flex:1;display:grid;grid-template-columns:150px 1fr;min-height:0}
.nav{padding:14px 10px;border-right:1px solid #ecedeb;background:#fbfbfa}
.n{font-size:13px;color:#5d6166;padding:7px 10px;border-radius:6px;margin-bottom:2px}
.n.on{background:#e8f3ed;color:#23744f;font-weight:600}
.ng{font-size:11px;color:#9a9ea2;padding:12px 10px 4px}
.c{padding:18px 24px;overflow:hidden}
.c h2{font-size:17px;margin-bottom:12px}
.grp{background:#fff;border:1px solid #ebecea;border-radius:8px;margin-bottom:14px}
.rw{display:flex;align-items:center;padding:0 14px;min-height:46px;border-top:1px solid #f1f1ef;font-size:13px}
.rw:first-child{border-top:0}.rw span{margin-right:auto}
.rw small{display:block;color:#8a8e92;font-size:11.5px}
.sw{width:34px;height:18px;border-radius:9px;background:#d5d8d5;position:relative}.sw:after{content:"";position:absolute;width:14px;height:14px;border-radius:50%;background:#fff;top:2px;left:2px}
.sw.on{background:#2f8f63}.sw.on:after{left:18px}
.sel{font-size:12.5px;border:1px solid #dfe1de;border-radius:6px;padding:4px 10px;display:flex;gap:16px;align-items:center;color:#3a3d40}
.sel svg{width:12px;height:12px;color:#8a8e92}
.gt{font-size:12px;color:#7a7e82;margin:0 0 6px 2px}
'''
nav=''.join(f'<div class="n{" on" if n=="通用" else ""}">{n}</div>' for n in ['通用','账号','Java','下载与网络'])+''.join(f'<div class="n">{n}</div>' for n in ['服务器','游戏时长','反馈与关于'])
def sel(t): return f'<div class="sel">{t}{ic("chev","i",12)}</div>'
st=f'''<div class="wrap"><div class="nav">{nav}</div><div class="c"><h2>通用</h2>
<div class="gt">外观</div><div class="grp">
<div class="rw"><span>深色模式</span><div class="sw"></div></div>
<div class="rw"><span>语言</span>{sel('简体中文')}</div>
<div class="rw"><span>背景</span>{sel('纯色')}</div></div>
<div class="gt">启动</div><div class="grp">
<div class="rw"><span>启动后<small>游戏窗口出现后启动器的行为</small></span>{sel('最小化')}</div>
<div class="rw"><span>自动检查更新</span><div class="sw on"></div></div></div>
</div></div>'''
open('settings.html','w').write(page(st_css,'设置',st,hot=True))

# --------- Close button detail
det='''<!doctype html><meta charset=utf-8><link rel=stylesheet href=common.css><style>body{width:auto;padding:24px;display:flex;gap:28px;background:#d9dde3;font-size:12px}
.d{display:flex;flex-direction:column;gap:8px;align-items:center}.box{background:#fff;border:1px solid #d6d8db;border-radius:8px;overflow:hidden;display:flex;transform:scale(2);transform-origin:top left;margin:0 140px 44px 0}
.old{font-family:"Segoe UI";font-size:10px;width:44px;height:34px;display:grid;place-items:center;margin:7px 0}.old.h{background:#E81123;color:#fff;border-radius:6px;margin:7px 2px;width:40px}</style>'''
def caps(hot): return f'''<div class="cap"><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></div><div class="cap"><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9" rx="1.5"/></svg></div><div class="cap x{' hot' if hot else ''}"><svg viewBox="0 0 10 10"><path d="M.5.5l9 9M9.5.5l-9 9"/></svg></div>'''
det+=f'<div class="d"><div class="box">{caps(False)}</div>新：常态</div><div class="d"><div class="box">{caps(True)}</div>新：悬停（贴边、到顶，跟随窗口圆角）</div>'
open('close.html','w').write(det)

I['plus']='<path d="M8 3v10M3 8h10"/>'
I['more']='<circle cx="3.5" cy="8" r=".9" fill="currentColor"/><circle cx="8" cy="8" r=".9" fill="currentColor"/><circle cx="12.5" cy="8" r=".9" fill="currentColor"/>'
I['back']='<path d="M10 3.5 5.5 8l4.5 4.5"/>'
I['folder']='<path d="M2 4.5h4l1.3 1.5H14v6.5H2z"/>'
common_list='''
.hd{display:flex;align-items:center;gap:8px;padding:16px 20px 12px}
.hd h2{font-size:17px;margin-right:auto}
.in{width:220px;height:32px;border:1px solid #dfe1de;border-radius:6px;background:#fff;display:flex;align-items:center;gap:8px;padding:0 10px;color:#9a9ea2;font-size:13px}
.btn.sm{height:32px;padding:0 12px;font-size:12.5px}
.btn.p2{background:#2f8f63;border-color:#2f8f63;color:#fff;font-weight:600}
.list{margin:0 20px;background:#fff;border:1px solid #ebecea;border-radius:8px;overflow:hidden}
.v{display:flex;align-items:center;gap:12px;padding:0 14px;height:52px;border-top:1px solid #f1f1ef;font-size:13px}
.v:first-child{border-top:0}
.v .b{width:30px;height:30px;border-radius:6px;background:#e9ebe8;display:grid;place-items:center;font-size:11.5px;color:#5d6166;font-weight:600;flex:none}
.v .nm{flex:1;min-width:0}.v .nm div{font-weight:600}.v .nm small{color:#8a8e92;font-size:11.5px}
.v .act{display:flex;gap:4px;align-items:center;color:#6b6f73}
.v .go{font-size:12.5px;color:#23744f;background:#e8f3ed;border-radius:6px;padding:5px 12px;display:flex;align-items:center;gap:5px}
.v.cur{background:#fbfdfb}
.sw{width:34px;height:18px;border-radius:9px;background:#d5d8d5;position:relative;flex:none}.sw:after{content:"";position:absolute;width:14px;height:14px;border-radius:50%;background:#fff;top:2px;left:2px}
.sw.on{background:#2f8f63}.sw.on:after{left:18px}
.sub{display:flex;align-items:center;gap:2px}
.s{font-size:13px;padding:6px 11px;border-radius:6px;color:#5d6166}
.s.on{background:#e8f3ed;color:#23744f;font-weight:600}
.tg{font-size:11px;padding:1px 7px;border-radius:4px;background:#eef0ee;color:#5d6166}
'''
# 实例库
inst=[('F','Fabric 1.20.1','Fabric 0.15 · 32 个模组 · 2 小时前',True),('V','1.21.4','原版 · 昨天',False),('N','NeoForge 整合包','NeoForge 21.1 · 148 个模组 · 3 天前',False),('O','1.12.2 OptiFine','OptiFine HD U G5 · 上个月',False)]
rows=''.join(f'''<div class="v{' cur' if c else ''}"><div class="b">{b}</div><div class="nm"><div>{n}</div><small>{m}</small></div>
<div class="act">{'<span class="tg" style="background:#e8f3ed;color:#23744f">当前</span>' if c else ''}<div class="ib">{ic('more')}</div><div class="go">{ic('play','i',12)}启动</div></div></div>''' for b,n,m,c in inst)
lib=f'''<div class="hd"><h2>实例库</h2><div class="in">{ic('search','i',14)}搜索实例</div><div class="btn sm">{ic('folder','i',14)}添加文件夹</div><div class="btn sm p2">{ic('plus','i',14)}新建</div></div>
<div class="list">{rows}</div>
<div style="padding:10px 22px;font-size:12px;color:#8a8e92">游戏目录 E:\\.minecraft</div>'''
open('library.html','w').write(page(common_list,'实例库',lib))

# 实例详情（模组管理移到这里）
det_css=common_list+'''
.top{display:flex;align-items:center;gap:10px;padding:14px 20px 4px}
.top h2{font-size:17px}.top .meta{color:#8a8e92;font-size:12px;margin-right:auto}
.bar2{display:flex;align-items:center;gap:8px;padding:10px 20px}
.v{height:46px}
'''
mods=[('Sodium','0.5.8',True,''),('Lithium','0.11.2',True,''),("Xaero's Minimap",'24.1.1',True,'有更新'),('Fabric API','0.92.0',True,''),('Iris Shaders','1.7.0',False,'')]
mr=''.join(f'''<div class="v"><div class="b">{n[0]}</div><div class="nm"><div>{n}</div><small>{v}</small></div>
{'<span class="tg" style="background:#fff4e0;color:#9a6200">'+u+'</span>' if u else ''}<div class="sw{' on' if on else ''}"></div><div class="ib">{ic('more')}</div></div>''' for n,v,on,u in mods)
det=f'''<div class="top"><div class="ib">{ic('back')}</div><h2>Fabric 1.20.1</h2><span class="meta">Fabric 0.15 · Java 17</span><div class="btn sm p2">{ic('play','i',12)}启动</div></div>
<div class="bar2"><div class="sub"><div class="s on">模组 32</div><div class="s">存档 5</div><div class="s">资源包</div><div class="s">光影</div><div class="s">设置</div></div><div style="margin-left:auto" class="in">{ic('search','i',14)}搜索模组</div><div class="btn sm">{ic('plus','i',14)}添加</div></div>
<div class="list">{mr}</div>'''
open('instance.html','w').write(page(det_css,'实例库',det))

# 插件
plg=[('A','AI 存档管家','让 AI 帮你整理和备份存档 · v1.2.0',True,''),('S','截图墙','游戏截图自动归档成相册 · v0.4.1',True,'有更新'),('H','Hello Plugin','示例插件 · 开发链接',False,'开发')]
pr=''.join(f'''<div class="v"><div class="b">{b}</div><div class="nm"><div>{n}</div><small>{m}</small></div>
{'<span class="tg'+('" style="background:#fff4e0;color:#9a6200"' if u=='有更新' else '"')+'>'+u+'</span>' if u else ''}<div class="sw{' on' if on else ''}"></div><div class="ib">{ic('more')}</div></div>''' for b,n,m,on,u in plg)
pl=f'''<div class="hd"><div class="sub" style="margin-right:auto"><div class="s on">已安装 3</div><div class="s">市场</div></div><div class="btn sm">{ic('plus','i',14)}从文件安装</div><div class="ib">{ic('more')}</div></div>
<div class="list">{pr}</div>
<div style="padding:10px 22px;font-size:12px;color:#8a8e92">也可以直接把插件 zip 拖进这个页面</div>'''
open('plugins.html','w').write(page(common_list,'插件',pl))

# 实例库：卡片版
card_css=common_list+'''
.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;padding:0 20px}
.cd{background:#fff;border:1px solid #ebecea;border-radius:8px;padding:12px 12px 10px;display:flex;flex-direction:column;gap:10px}
.cd.cur{border-color:#bfe0cd}
.ct{display:flex;align-items:center;gap:10px}
.ct .b{width:34px;height:34px;border-radius:6px;background:#e9ebe8;display:grid;place-items:center;font-size:12px;color:#5d6166;font-weight:600;flex:none}
.ct .nm{flex:1;min-width:0}.ct .nm div{font-weight:600;font-size:13.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.ct .nm small{color:#8a8e92;font-size:11.5px}
.cb{display:flex;align-items:center;gap:6px;font-size:11.5px;color:#8a8e92}
.cb .go{margin-left:auto;font-size:12.5px;color:#23744f;background:#e8f3ed;border-radius:6px;padding:5px 12px;display:flex;align-items:center;gap:5px}
.cb .ib{width:28px;height:28px}
.new{border:1px dashed #d3d6d2;border-radius:8px;display:flex;align-items:center;justify-content:center;gap:6px;color:#7a7e82;font-size:13px;min-height:104px}
'''
inst2=[('F','Fabric 1.20.1','Fabric 0.15','32 个模组 · 2 小时前',True),('V','1.21.4','原版','昨天',False),('N','NeoForge 整合包','NeoForge 21.1','148 个模组 · 3 天前',False),('O','1.12.2 OptiFine','OptiFine HD U G5','上个月',False),('Q','Quilt 1.20.4','Quilt 0.26','12 个模组 · 上个月',False)]
cards=''.join(f'''<div class="cd{' cur' if c else ''}"><div class="ct"><div class="b">{b}</div><div class="nm"><div>{n}</div><small>{l}</small></div>{'<span class="tg" style="background:#e8f3ed;color:#23744f">当前</span>' if c else ''}</div>
<div class="cb"><span style="margin-right:auto">{m}</span><div class="ib">{ic('more')}</div><div class="go" style="margin-left:0">{ic('play','i',12)}启动</div></div></div>''' for b,n,l,m,c in inst2)
lib2=f'''<div class="hd"><h2>实例库</h2><div class="in">{ic('search','i',14)}搜索实例</div><div class="btn sm">{ic('folder','i',14)}添加文件夹</div><div class="btn sm p2">{ic('plus','i',14)}新建</div></div>
<div class="grid">{cards}<div class="new">{ic('plus','i',14)}新建实例</div></div>
<div style="padding:12px 22px;font-size:12px;color:#8a8e92">游戏目录 E:\\.minecraft</div>'''
open('library.html','w').write(page(card_css,'实例库',lib2))
