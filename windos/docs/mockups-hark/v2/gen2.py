A='--a:#c9403f;--ag:rgba(201,64,63,.28);--as:#fdeeee;--ab:#f5cfcf'
I = {
'spark':'<path d="M8 1.5v3M8 11.5v3M1.5 8h3M11.5 8h3M3.4 3.4l2 2M10.6 10.6l2 2M3.4 12.6l2-2M10.6 5.4l2-2"/>',
'moon':'<path d="M13 9.5A5.5 5.5 0 0 1 6.5 3a5.5 5.5 0 1 0 6.5 6.5z"/>',
'play':'<path d="M5 3.5v9l7-4.5z"/>',
'playf':'<path d="M5 3.5v9l7-4.5z" fill="currentColor"/>',
'sliders':'<path d="M2.5 4.5h7M12.5 4.5h1M2.5 11.5h2M7.5 11.5h6"/><circle cx="11" cy="4.5" r="1.5"/><circle cx="6" cy="11.5" r="1.5"/>',
'wifi':'<path d="M2 6.5a9 9 0 0 1 12 0M4.2 9a5.6 5.6 0 0 1 7.6 0M6.4 11.4a2.3 2.3 0 0 1 3.2 0"/><circle cx="8" cy="13.4" r=".6" fill="currentColor"/>',
'down':'<path d="M8 2.5v8M4.8 7.6 8 10.8l3.2-3.2M3 13.5h10"/>',
'search':'<circle cx="7" cy="7" r="4.3"/><path d="m10.3 10.3 3.2 3.2"/>',
'chev':'<path d="m4.5 6.5 3.5 3.5 3.5-3.5"/>','right':'<path d="m6 4 4 4-4 4"/>',
'plus':'<path d="M8 3v10M3 8h10"/>','back':'<path d="M10 3.5 5.5 8l4.5 4.5"/>',
'more':'<circle cx="3.5" cy="8" r=".9" fill="currentColor"/><circle cx="8" cy="8" r=".9" fill="currentColor"/><circle cx="12.5" cy="8" r=".9" fill="currentColor"/>',
}
def ic(n,w=16): return f'<svg class="i" viewBox="0 0 16 16" width="{w}" height="{w}">{I[n]}</svg>'
def tb(active,glass=False,hot=False):
    tabs=''.join(f'<div class="tab{" on" if t==active else ""}">{t}</div>' for t in ['启动','实例库','下载','插件','设置'])
    return f'''<div class="tb{' glass' if glass else ''}"><div class="logo"><i>T</i>TipCL</div><div class="tabs">{tabs}</div><div class="sp"></div>
<div class="tbr"><div class="ib">{ic('spark')}</div><div class="ib">{ic('moon')}</div><div class="av"><b>1</b>111</div><div class="sep"></div>
<div class="cap"><svg viewBox="0 0 10 10"><path d="M0 5h10"/></svg></div><div class="cap"><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9" rx="1.5"/></svg></div>
<div class="cap{' hot' if hot else ''}"><svg viewBox="0 0 10 10"><path d="M.5.5l9 9M9.5.5l-9 9"/></svg></div></div></div>'''
def page(name,css,active,body,glass=False,hot=False,winstyle=''):
    open(name+'.html','w').write(f'<!doctype html><meta charset=utf-8><link rel=stylesheet href=style2.css><style>:root{{{A}}}{css}</style><div class="win" style="{winstyle}">{tb(active,glass,hot)}<div class="body">{body}</div></div>')

# 启动
css='''.win{background:#1b2230}
.bg{position:absolute;inset:0;background:url(hero.jpg) center/cover}
.sh{position:absolute;inset:0;background:linear-gradient(90deg,rgba(10,14,22,.78) 0%,rgba(10,14,22,.45) 42%,rgba(10,14,22,0) 75%),linear-gradient(0deg,rgba(10,14,22,.55),rgba(10,14,22,0) 35%)}
.win>.tb{position:absolute;left:0;right:0;top:0}
.body{padding-top:46px;color:#fff}
.l{position:relative;flex:1;display:flex;flex-direction:column;justify-content:center;padding-left:56px}
.k{font-size:12.5px;color:rgba(255,255,255,.8);display:flex;align-items:center;gap:7px}
.k .dot{background:#4ade80;box-shadow:0 0 8px #4ade80}
.t{font-size:34px;font-weight:800;margin:6px 0 2px;display:flex;align-items:center;gap:10px;letter-spacing:-.3px;text-shadow:0 2px 12px rgba(0,0,0,.3)}
.t svg{color:rgba(255,255,255,.7)}
.chips{display:flex;gap:6px;margin:10px 0 24px}
.chip{font-size:12px;padding:3px 10px;border-radius:7px;background:rgba(255,255,255,.14);backdrop-filter:blur(10px);border:1px solid rgba(255,255,255,.18)}
.row{display:flex;gap:10px;align-items:center}
.go{height:48px;border-radius:12px;padding:0 30px;background:var(--a);display:flex;align-items:center;gap:10px;font-size:16px;font-weight:800;box-shadow:0 6px 20px rgba(201,64,63,.45)}
.gl{height:48px;border-radius:12px;background:rgba(255,255,255,.14);backdrop-filter:blur(12px);border:1px solid rgba(255,255,255,.22);display:flex;align-items:center;gap:8px;padding:0 16px;font-size:14px;font-weight:600}
.gl.sq{width:48px;padding:0;justify-content:center}
.news{position:relative;margin:0 0 18px 56px;align-self:flex-start;display:flex;align-items:center;gap:10px;padding:8px 14px 8px 8px;border-radius:12px;background:rgba(255,255,255,.12);backdrop-filter:blur(14px);border:1px solid rgba(255,255,255,.18);font-size:12.5px}
.news b{font-size:11px;background:var(--a);padding:2px 8px;border-radius:6px}
'''
body=f'''<div class="l"><div class="k"><span class="dot"></span>就绪 · E:\\.minecraft</div>
<div class="t">Fabric 1.20.1 {ic('chev',20)}</div>
<div class="chips"><span class="chip">Java 17</span><span class="chip">4 GB</span><span class="chip">32 个模组</span></div>
<div class="row"><div class="go">{ic('playf',18)}启动游戏</div><div class="gl sq">{ic('sliders',18)}</div><div class="gl">{ic('wifi',17)}联机</div></div></div>
<div class="news"><b>新闻</b>Minecraft 26.4 Snapshot 7 已发布 {ic('right',12)}</div>'''
page('launch2',css,'启动','<div class="bg"></div><div class="sh"></div>'+body,glass=True)

# 实例库：保持现有卡片风格
css='''.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;padding:0 22px}
.ic{padding:16px 18px 16px}
.ic h3{font-size:15px;font-weight:800;display:flex;align-items:center;gap:9px}
.ic p{font-size:12.5px;color:#888;margin:10px 0 12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.ic .r{display:flex;gap:8px}'''
inst=[('.minecraft','Minecraft fabric-loader-0.20.1-26.3 · 2 个版本',True),('生存 1.21.4','Minecraft 1.21.4 · Java 自动选择',False),('NeoForge 整合包','NeoForge 21.1 · 148 个模组',False),('1.12.2 OptiFine','OptiFine HD U G5 · Java 8',False)]
cards=''.join(f'<div class="card ic"><h3><span class="dot" style="{"" if c else "background:#cfcfcf"}"></span>{n}</h3><p>{m}</p><div class="r"><div class="bs">{ic("play",13)}启动</div><div class="bn">管理</div></div></div>' for n,m,c in inst)
page('library2',css,'实例库',f'<div class="hd"><h2>实例库</h2><div class="in">搜索实例…</div><div class="bp">{ic("plus",14)}新建实例</div></div><div class="grid">{cards}</div>')

# 下载
css='''.top{display:flex;align-items:center;padding:16px 22px 12px}
.seg{background:rgba(0,0,0,.05);padding:3px;border-radius:10px}
.task{margin-left:auto;display:flex;align-items:center;gap:6px;font-size:13px;font-weight:600;padding:6px 12px;border-radius:8px;background:#fff;box-shadow:0 1px 3px rgba(0,0,0,.06)}
.task b{background:var(--a);color:#fff;font-size:11px;border-radius:8px;padding:0 6px}
.bar{display:flex;gap:8px;padding:0 22px 12px;align-items:center}.bar .in{flex:1;width:auto}
.f{font-size:13px;padding:6px 12px;border-radius:8px;color:#666}.f.on{background:var(--as);color:var(--a);font-weight:700}
.list{margin:0 22px;padding:4px 0}
.v{display:flex;align-items:center;padding:0 18px;height:46px;font-size:13.5px;border-top:1px solid #f2f2f2}.v:first-child{border-top:0}
.v .n{width:130px;font-weight:700}.v .tg{margin-right:auto}.v .tg.new{background:var(--as);color:var(--a)}
.v .d{color:#999;font-size:12.5px;width:110px}.v.hv{background:#fafafa}.v.hv .bs{background:var(--a);color:#fff;border-color:var(--a)}'''
rows=[('26.4-snap7','快照','new','2026-11-05'),('1.21.10','正式版','new','2026-10-07'),('1.21.9','正式版','','2026-09-30'),('1.21.8','正式版','','2026-07-17'),('1.20.1','正式版','','2023-06-12'),('1.12.2','正式版','','2017-09-18')]
vr=''.join(f'<div class="v{" hv" if a=="1.20.1" else ""}"><span class="n">{a}</span><span class="tg {c}">{b}</span><span class="d">{d}</span><div class="bs">{ic("down",13)}安装</div></div>' for a,b,c,d in rows)
seg=''.join(f'<div class="sg{" on" if s=="原版" else ""}">{s}</div>' for s in ['原版','Mod','整合包','数据包','资源包','光影','世界'])
page('download2',css,'下载',f'<div class="top"><div class="seg">{seg}</div><div class="task">{ic("down",14)}任务<b>2</b></div></div><div class="bar"><div class="in">{ic("search",14)}搜索版本</div><div class="f on">正式版</div><div class="f">快照</div><div class="f">远古</div></div><div class="card list">{vr}</div>')

# 插件：同实例库卡片
css='''.hd .seg{background:rgba(0,0,0,.05);padding:3px;border-radius:10px;margin-right:auto}
.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;padding:0 22px}
.pc{padding:16px 18px}
.pc .h{display:flex;align-items:center;gap:10px}
.pc .lg{width:36px;height:36px;border-radius:9px;display:grid;place-items:center;color:#fff;font-weight:800;font-size:14px}
.pc h3{font-size:15px;font-weight:800;margin-right:auto}
.pc p{font-size:12.5px;color:#888;margin:10px 0 12px;height:36px;overflow:hidden}
.pc .r{display:flex;align-items:center;gap:8px;font-size:12px;color:#999}.pc .r span{margin-right:auto}
.ib2{width:30px;height:30px;border-radius:7px;display:grid;place-items:center;color:#777}'''
pl=[('AI 存档管家','让 AI 帮你整理和备份存档，回滚前自动快照','v1.2.0','#7c5cff',True,''),('截图墙','游戏截图自动归档成相册，可一键分享','v0.4.1','#f59e0b',True,'有更新'),('Hello Plugin','示例插件，演示页面、AI 工具和启动钩子','开发链接','#64748b',False,'开发')]
pc=''.join(f'''<div class="card pc"><div class="h"><div class="lg" style="background:{c}">{n[0]}</div><h3>{n}</h3><div class="sw{' on' if on else ''}"></div></div><p>{d}</p>
<div class="r"><span>{v}</span>{'<span class="tg" style="background:#fff4e0;color:#b26b00;margin-right:0">'+u+'</span>' if u else ''}<div class="ib2">{ic('more')}</div></div></div>''' for n,d,v,c,on,u in pl)
page('plugins2',css,'插件',f'<div class="hd"><div class="seg"><div class="sg on">已安装 3</div><div class="sg">市场</div></div><div class="bn" style="height:34px;border-radius:8px;background:#fff;border-color:#e3e3e3">从文件安装</div><div class="ib">{ic("more")}</div></div><div class="grid">{pc}</div>')

# 设置
css='''.wrap{flex:1;display:grid;grid-template-columns:168px 1fr;min-height:0}
.nav{padding:16px 12px}
.n{font-size:13.5px;color:#666;padding:8px 12px;border-radius:8px;margin-bottom:2px;font-weight:500}
.n.on{background:#fff;color:#1f1f1f;font-weight:700;box-shadow:0 1px 3px rgba(0,0,0,.06)}
.c{padding:16px 22px 0 6px}
.c h2{font-size:21px;font-weight:800;margin-bottom:12px}
.gt{font-size:12.5px;color:#888;margin:0 0 6px 4px;font-weight:600}
.grp{margin-bottom:14px}
.rw{display:flex;align-items:center;padding:0 18px;min-height:48px;border-top:1px solid #f2f2f2;font-size:13.5px}.rw:first-child{border-top:0}.rw>span{margin-right:auto}
.rw small{display:block;color:#999;font-size:11.5px}
.sel{font-size:13px;border:1px solid #e3e3e3;border-radius:8px;padding:5px 12px;display:flex;gap:18px;align-items:center}'''
nav=''.join(f'<div class="n{" on" if n=="通用" else ""}">{n}</div>' for n in ['通用','账号','Java','下载与网络','服务器','游戏时长','反馈与关于'])
sel=lambda t:f'<div class="sel">{t}{ic("chev",12)}</div>'
page('settings2',css,'设置',f'''<div class="wrap"><div class="nav">{nav}</div><div class="c"><h2>通用</h2>
<div class="gt">外观</div><div class="card grp"><div class="rw"><span>深色模式</span><div class="sw"></div></div><div class="rw"><span>主题色</span><div style="display:flex;gap:6px"><span class="dot" style="width:16px;height:16px;box-shadow:0 0 0 2px #fff,0 0 0 3.5px var(--a)"></span><span class="dot" style="width:16px;height:16px;background:#2f8f63"></span><span class="dot" style="width:16px;height:16px;background:#3b82f6"></span><span class="dot" style="width:16px;height:16px;background:#8b5cf6"></span></div></div><div class="rw"><span>语言</span>{sel("简体中文")}</div></div>
<div class="gt">启动</div><div class="card grp"><div class="rw"><span>启动后<small>游戏窗口出现后启动器的行为</small></span>{sel("最小化")}</div><div class="rw"><span>自动检查更新</span><div class="sw on"></div></div></div></div></div>''',hot=True)

# 安装页
css='''.hd2{display:flex;align-items:center;gap:10px;padding:14px 22px 10px}
.bk{width:32px;height:32px;border-radius:8px;background:#fff;display:grid;place-items:center;box-shadow:0 1px 3px rgba(0,0,0,.06)}
.hd2 h2{font-size:20px;font-weight:800}.hd2 .tg{background:#e8f5ee;color:#2f8f63}
.sc{flex:1;padding:0 22px;overflow:hidden}
.lb{font-size:12.5px;color:#888;font-weight:600;margin:8px 0 7px 2px}
.nm{background:#fff;border-radius:10px;height:38px;display:flex;align-items:center;padding:0 14px;font-size:13.5px;font-weight:600;border:1px solid #e3e3e3}
.nm small{margin-left:auto;color:#aaa;font-weight:400;font-size:12px}
.lds{display:grid;grid-template-columns:repeat(5,1fr);gap:10px}
.ld{display:flex;align-items:center;gap:10px;background:#fff;border-radius:12px;padding:11px 12px;border:1.5px solid transparent;box-shadow:0 1px 3px rgba(0,0,0,.04);position:relative}
.ld b{display:block;font-size:14px;font-weight:800}.ld span{font-size:11.5px;color:#999}
.ld.on{border-color:var(--a);background:#fffafa}
.ld.on:after{content:"✓";position:absolute;right:10px;top:9px;width:16px;height:16px;border-radius:50%;background:var(--a);color:#fff;font-size:10px;display:grid;place-items:center;font-weight:800}
.ld.off{opacity:.45}.li{width:36px;height:36px;border-radius:9px;display:grid;place-items:center;flex:none;overflow:hidden}
.vbox{margin-top:10px;background:#fff;border-radius:12px;box-shadow:0 1px 3px rgba(0,0,0,.04);padding:4px 0}
.vr{display:flex;align-items:center;padding:0 16px;height:42px;font-size:13.5px;border-top:1px solid #f2f2f2}.vr:first-child{border-top:0}
.vr>span{margin-right:auto}.vr small{color:#999;font-size:12px;margin-left:8px}
.sel{font-size:13px;border:1px solid #e3e3e3;border-radius:8px;padding:5px 12px;display:flex;gap:14px;align-items:center;font-weight:600}
.dis{color:#aaa}
.foot{display:flex;align-items:center;gap:12px;padding:12px 22px;background:rgba(255,255,255,.85);border-top:1px solid rgba(0,0,0,.06);font-size:13px;color:#666}
.foot b{color:#1f1f1f}.foot .bp{margin-left:auto;height:38px;padding:0 20px;font-size:14px}'''
lds=[('不安装','纯原版',''),('Fabric','0.16.10','on'),('Forge','47.3.0',''),('NeoForge','47.1.106',''),('Quilt','0.27.1','')]
logos={'不安装':('grass.png','#f1f5ee',30),'Fabric':('fabric_gh.png','#f5f1e8',30),'Forge':('forge_gh.png','#1d2a3e',36),'NeoForge':('neoforge_gh.png','#f6ede4',30),'Quilt':('quilt.svg','#f2edf8',26)}
ldh=''.join(f'<div class="ld {c}"><div class="li" style="background:{logos[a][1]}"><img src="{logos[a][0]}" style="width:{logos[a][2]}px;height:{logos[a][2]}px;image-rendering:{"pixelated" if a in ("Fabric","NeoForge") else "auto"}"></div><div><b>{a}</b><span>{b}</span></div></div>' for a,b,c in lds)
page('install2',css,'下载',f'''<div class="hd2"><div class="bk">{ic('back')}</div><h2>安装 Minecraft 1.20.1</h2><span class="tg">正式版</span></div>
<div class="sc"><div class="lb">实例名称</div><div class="nm">Fabric 1.20.1<small>安装到 E:\\.minecraft</small></div>
<div class="lb" style="margin-top:14px">模组加载器</div><div class="lds">{ldh}</div>
<div class="vbox"><div class="vr"><span>Fabric 版本</span>{sel('0.16.10 · 推荐')}</div>
<div class="vr"><span>Fabric API<small>大多数 Fabric 模组都需要</small></span><span class="muted" style="font-size:12.5px;margin-right:12px">0.92.2</span><div class="sw on"></div></div>
<div class="vr dis"><span>OptiFine<small>与 Fabric 不兼容，可改装 Sodium + Iris</small></span><div class="sw" style="opacity:.5"></div></div></div></div>
<div class="foot">将安装 <b>1.20.1 + Fabric 0.16.10 + Fabric API</b> · 约 32 MB<div class="bp">{ic('down',15)}开始安装</div></div>''')

