"""AI3-visual: build the visual comparison report.

    python tools/visual/compare.py [--visual <_visual dir>] [--repo <repo root>] [--tour <tour dir>]

For every pair in pairs.json (mockup / reference image <-> tour screenshot):
  * both images are scaled to the same width (W=600) and the mockup's aspect ratio,
  * structural similarity (SSIM on luma, 10x10 blocks) and colour difference (mean RGB distance per
    block + dominant-colour distance) are computed,
  * a heat map marks the blocks that differ most (the three worst regions get a frame),
  * score = 100 * (0.6 * SSIM + 0.4 * (1 - colour difference)).
Rows are sorted worst-first into <_visual>/report/index.html. The score is only a hint; the review
verdicts live in findings.md (written by a human / the agent after looking at every pair).
Only the standard library is required (vtlib uses Pillow if it happens to be importable, for speed).
"""
import argparse
import html
import json
import math
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vtlib  # noqa: E402

W = 600
BLOCK = 10


def resolve(p, visual, repo):
    if p.startswith('@visual/'):
        return os.path.join(visual, *p[len('@visual/'):].split('/'))
    if p.startswith('@repo/'):
        return os.path.join(repo, *p[len('@repo/'):].split('/'))
    return p


def fit(img, w, h):
    return vtlib.resize(img, w, h)


def block_stats(g, w, h, x0, y0, bw, bh):
    vals = []
    for y in range(y0, y0 + bh):
        row = y * w
        vals.extend(g[row + x0:row + x0 + bw])
    n = len(vals)
    m = sum(vals) / n
    v = sum((t - m) * (t - m) for t in vals) / n
    return vals, m, v


def compare(a, b):
    """a, b: same-size images. Returns (ssim_mean, color_diff, blocks) with blocks[(bx,by)] = dissimilarity 0..1."""
    w, h, ra = a
    _, _, rb = b
    ga, gb = vtlib.gray(a), vtlib.gray(b)
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    nbx, nby = w // BLOCK, h // BLOCK
    ssims, colors, blocks = [], [], {}
    for by in range(nby):
        for bx in range(nbx):
            x0, y0 = bx * BLOCK, by * BLOCK
            va, ma, sa = block_stats(ga, w, h, x0, y0, BLOCK, BLOCK)
            vb, mb, sb = block_stats(gb, w, h, x0, y0, BLOCK, BLOCK)
            cov = sum((p - ma) * (q - mb) for p, q in zip(va, vb)) / len(va)
            s = ((2 * ma * mb + c1) * (2 * cov + c2)) / ((ma * ma + mb * mb + c1) * (sa + sb + c2))
            # colour: mean RGB of the block
            sr = [0, 0, 0]
            tr = [0, 0, 0]
            for y in range(y0, y0 + BLOCK):
                for x in range(x0, x0 + BLOCK, 2):
                    i = (y * w + x) * 3
                    for k in range(3):
                        sr[k] += ra[i + k]
                        tr[k] += rb[i + k]
            n = BLOCK * BLOCK / 2
            cd = math.sqrt(sum((sr[k] / n - tr[k] / n) ** 2 for k in range(3))) / 441.7
            ssims.append(s)
            colors.append(cd)
            blocks[(bx, by)] = min(1.0, max(0.0, (1 - s) * 0.6 + cd * 1.6))
    return sum(ssims) / len(ssims), sum(colors) / len(colors), blocks, nbx, nby


def dominant(img, k=4):
    w, h, rgb = img
    hist = {}
    for i in range(0, w * h * 3, 3 * 7):
        key = (rgb[i] >> 4, rgb[i + 1] >> 4, rgb[i + 2] >> 4)
        hist[key] = hist.get(key, 0) + 1
    top = sorted(hist.items(), key=lambda kv: -kv[1])[:k]
    total = sum(hist.values())
    return [('#%02x%02x%02x' % (c[0] * 16 + 8, c[1] * 16 + 8, c[2] * 16 + 8), n / total) for c, n in top]


def hexdist(a, b):
    pa = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    pb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(pa, pb))) / 441.7


def worst_regions(blocks, nbx, nby, n=3, r=3):
    """Group blocks into r x r regions and return the n worst (x, y, w, h in blocks, score)."""
    regs = []
    for ry in range(0, nby, r):
        for rx in range(0, nbx, r):
            vals = [blocks.get((x, y), 0) for y in range(ry, min(nby, ry + r)) for x in range(rx, min(nbx, rx + r))]
            regs.append((sum(vals) / len(vals), rx, ry, min(r, nbx - rx), min(r, nby - ry)))
    regs.sort(reverse=True)
    out = []
    for s, rx, ry, rw, rh in regs:
        if s < 0.12:
            break
        if any(abs(rx - o[0]) <= r and abs(ry - o[1]) <= r for o in out):
            continue
        out.append((rx, ry, rw, rh, s))
        if len(out) >= n:
            break
    return out


def heatmap(shot, blocks, nbx, nby, regions):
    w, h, rgb = shot
    out = bytearray(len(rgb))
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 3
            g = (rgb[i] * 299 + rgb[i + 1] * 587 + rgb[i + 2] * 114) // 1000
            g = 70 + g * 120 // 255
            d = blocks.get((min(nbx - 1, x // BLOCK), min(nby - 1, y // BLOCK)), 0)
            a = min(1.0, d * 1.4)
            out[i] = int(g * (1 - a) + 235 * a)
            out[i + 1] = int(g * (1 - a) + 40 * a)
            out[i + 2] = int(g * (1 - a) + 40 * a)
    for rx, ry, rw, rh, _ in regions:
        x0, y0, x1, y1 = rx * BLOCK, ry * BLOCK, min(w - 1, (rx + rw) * BLOCK), min(h - 1, (ry + rh) * BLOCK)
        for t in range(2):
            for x in range(x0, x1 + 1):
                for y in (min(h - 1, y0 + t), max(0, y1 - t)):
                    i = (y * w + x) * 3
                    out[i:i + 3] = b'\xff\xdd\x00'
            for y in range(y0, y1 + 1):
                for x in (min(w - 1, x0 + t), max(0, x1 - t)):
                    i = (y * w + x) * 3
                    out[i:i + 3] = b'\xff\xdd\x00'
    return w, h, out


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo_default = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser()
    ap.add_argument('--repo', default=repo_default)
    ap.add_argument('--visual', default=os.path.join(os.path.dirname(repo_default), '_visual'))
    ap.add_argument('--tour', default='')
    ap.add_argument('--pairs', default=os.path.join(here, 'pairs.json'))
    args = ap.parse_args()
    visual, repo = args.visual, args.repo
    tour = args.tour or os.path.join(visual, 'tour')
    report = os.path.join(visual, 'report')
    for d in ('img', 'heat', 'ref'):
        os.makedirs(os.path.join(report, d), exist_ok=True)

    pairs = json.load(open(args.pairs, encoding='utf-8'))['pairs']
    rows = []
    for p in pairs:
        mpath = resolve(p['mockup'], visual, repo)
        spath = os.path.join(tour, p['shot'] + '.png')
        row = dict(p, mockupPath=mpath, shotPath=spath)
        if not os.path.exists(mpath) or not os.path.exists(spath):
            row['error'] = 'missing ' + ('mockup' if not os.path.exists(mpath) else 'shot')
            rows.append(row)
            print('SKIP', p['id'], row['error'])
            continue
        m = vtlib.read_png(mpath)
        s = vtlib.read_png(spath)
        h = max(BLOCK, round(W * m[1] / m[0]))
        h -= h % BLOCK
        mm, ss = fit(m, W, h), fit(s, W, h)
        ssim, cdiff, blocks, nbx, nby = compare(mm, ss)
        regions = worst_regions(blocks, nbx, nby)
        dm, ds = dominant(mm), dominant(ss)
        domdiff = sum(min(hexdist(a, b) for b, _ in ds) * wa for a, wa in dm) / max(1e-6, sum(wa for _, wa in dm))
        score = 100 * (0.6 * max(0.0, ssim) + 0.4 * max(0.0, 1 - (cdiff * 0.7 + domdiff * 0.3) * 2.5))
        # copies for the report (mockup scaled, shot scaled, heat map)
        vtlib.write_png(os.path.join(report, 'ref', p['id'] + '.png'), *mm)
        vtlib.write_png(os.path.join(report, 'img', p['id'] + '.png'), *ss)
        vtlib.write_png(os.path.join(report, 'heat', p['id'] + '.png'), *heatmap(ss, blocks, nbx, nby, regions))
        row.update(score=round(score, 1), ssim=round(ssim, 3), color=round(cdiff, 3), dominant=round(domdiff, 3),
                   domM=dm, domS=ds, regions=[(rx * BLOCK, ry * BLOCK, rw * BLOCK, rh * BLOCK, round(sc, 2)) for rx, ry, rw, rh, sc in regions],
                   size=(m[0], m[1], s[0], s[1]))
        rows.append(row)
        print('%-28s score=%5.1f ssim=%.3f color=%.3f dom=%.3f' % (p['id'], score, ssim, cdiff, domdiff))

    rows.sort(key=lambda r: (r.get('score') is not None, r.get('score', 0)))
    json.dump(rows, open(os.path.join(report, 'scores.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1, default=str)

    # tour result (FAIL steps) and coverage gallery
    tr_path = os.path.join(tour, 'tour-result.json')
    tr = json.load(open(tr_path, encoding='utf-8')) if os.path.exists(tr_path) else {}
    paired = {r['shot'] for r in rows}
    gallery = sorted(f[:-4] for f in os.listdir(tour) if f.endswith('.png') and not f.endswith('.dialog.png')) if os.path.isdir(tour) else []
    for g in gallery:
        dst = os.path.join(report, 'img', 'tour-' + g + '.png')
        shutil.copyfile(os.path.join(tour, g + '.png'), dst)

    e = html.escape
    out = ['<!DOCTYPE html><html lang="zh-CN"><head><meta charset="utf-8"><title>TipCL 视觉巡检报告</title><style>',
           'body{font:13px/1.5 "Segoe UI","Microsoft YaHei",sans-serif;margin:24px;background:#f4f5f7;color:#1d2330}',
           'h1{font-size:20px;margin:0 0 4px}h2{font-size:16px;margin:28px 0 8px}.muted{color:#667}',
           'table{border-collapse:collapse;width:100%;background:#fff;border-radius:10px;overflow:hidden}',
           'td,th{border-bottom:1px solid #e6e8ec;padding:8px;vertical-align:top;text-align:left}',
           'th{background:#eef0f4;font-weight:600}img{width:300px;border:1px solid #dde;border-radius:6px;display:block}',
           '.score{font-size:22px;font-weight:700}.bad{color:#c62828}.mid{color:#d98a00}.good{color:#2e7d32}',
           '.sw{display:inline-block;width:12px;height:12px;border-radius:3px;margin-right:2px;vertical-align:-2px;border:1px solid #0002}',
           '.fail{color:#c62828;font-weight:600}.gal{display:flex;flex-wrap:wrap;gap:10px}.gal figure{margin:0;background:#fff;padding:6px;border-radius:8px}',
           '.gal img{width:260px}.gal figcaption{font-size:12px;color:#556}</style></head><body>',
           '<h1>TipCL 视觉巡检报告</h1>',
           '<p class="muted">效果图 | 实际截图(--visual-tour 无窗口渲染,固定假数据目录)| 差异热力图(两图缩放到同宽 %d px,'
           '%d×%d 块 SSIM + 主色差;黄框 = 差异最大的区域)。按分数从差到好排序。<b>分数只是辅助,结论见 '
           '<a href="findings.md">findings.md</a>(逐对看图审核)。</b> 修复清单:'
           '<a href="fixlist-launch.md">启动页</a> · <a href="fixlist-layout.md">布局 / 编辑态</a> · '
           '<a href="fixlist-download.md">下载 / 安装</a> · <a href="fixlist-library.md">实例库 / 详情</a> · '
           '<a href="fixlist-other.md">其它</a></p>' % (W, BLOCK, BLOCK)]
    if tr:
        sm = tr.get('summary', {})
        out.append('<p>巡检剧本:<code>%s</code> · 步骤 %s · OK %s · <span class="fail">FAIL %s</span> · 截图 %s · 假数据:%s</p>'
                   % (e(os.path.basename(tr.get('script', ''))), sm.get('steps'), sm.get('ok'), sm.get('fail'), sm.get('shots'), e(tr.get('fixture', ''))))
    out.append('<table><tr><th>#</th><th>界面</th><th>效果图 / 参照</th><th>实际截图</th><th>差异热力图</th><th>分数</th></tr>')
    for i, r in enumerate(rows, 1):
        if 'error' in r:
            out.append('<tr><td>%d</td><td><b>%s</b><br><span class="muted">%s</span></td><td colspan="4" class="fail">%s</td></tr>'
                       % (i, e(r['title']), e(r['src']), e(r['error'])))
            continue
        cls = 'bad' if r['score'] < 55 else 'mid' if r['score'] < 72 else 'good'
        sw = lambda d: ''.join('<span class="sw" style="background:%s" title="%s %.0f%%"></span>' % (c, c, f * 100) for c, f in d)
        regs = '<br>'.join('区域 @%d,%d %d×%d 差异 %.2f' % t for t in r['regions']) or '无明显集中差异'
        out.append('<tr id="%s"><td>%d</td><td><b>%s</b><br><span class="muted">%s<br>效果图 %d×%d · 截图 %d×%d<br>截图名 %s</span></td>'
                   '<td><a href="ref/%s.png"><img src="ref/%s.png"></a></td><td><a href="img/%s.png"><img src="img/%s.png"></a></td>'
                   '<td><a href="heat/%s.png"><img src="heat/%s.png"></a></td>'
                   '<td><div class="score %s">%.1f</div>SSIM %.3f<br>色差 %.3f · 主色差 %.3f<br>效果图主色 %s<br>实际主色 %s<br><span class="muted">%s</span></td></tr>'
                   % (e(r['id']), i, e(r['title']), e(r['src']), r['size'][0], r['size'][1], r['size'][2], r['size'][3], e(r['shot']),
                      r['id'], r['id'], r['id'], r['id'], r['id'], r['id'], cls, r['score'], r['ssim'], r['color'], r['dominant'],
                      sw(r['domM']), sw(r['domS']), regs))
    out.append('</table>')

    if tr:
        fails = [s for s in tr.get('steps', []) if s.get('Status') == 'FAIL']
        out.append('<h2>巡检剧本里的 FAIL 步骤(%d)</h2><table><tr><th>步</th><th>动作</th><th>原因</th><th>说明</th></tr>' % len(fails))
        for s in fails:
            out.append('<tr><td>%s</td><td><code>%s</code></td><td>%s</td><td>%s</td></tr>'
                       % (s.get('Index'), e(s.get('Step', ''))[:160], e(s.get('Message', '')), e(s.get('Note', ''))))
        out.append('</table>')

    out.append('<h2>全部截图(覆盖巡检,%d 张;无效果图对照的也在这里)</h2><div class="gal">' % len(gallery))
    for g in gallery:
        mark = '' if g in paired else ' · 无对照'
        out.append('<figure><a href="img/tour-%s.png"><img src="img/tour-%s.png"></a><figcaption>%s%s</figcaption></figure>' % (g, g, e(g), mark))
    out.append('</div></body></html>')
    open(os.path.join(report, 'index.html'), 'w', encoding='utf-8').write('\n'.join(out))
    print('report:', os.path.join(report, 'index.html'), 'pairs=%d shots=%d' % (len(rows), len(gallery)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
