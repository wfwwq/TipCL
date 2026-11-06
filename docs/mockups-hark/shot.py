from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b=p.chromium.launch(); pg=b.new_page(device_scale_factor=2)
    for n in ['launch','download','settings','library','instance','plugins']:
        pg.goto(f'file:///workspace/work/mock/{n}.html'); pg.wait_for_timeout(200)
        (pg.locator('.win').screenshot(path=f'/workspace/work/mock/{n}.png') if n!='close' else None)
    b.close()
