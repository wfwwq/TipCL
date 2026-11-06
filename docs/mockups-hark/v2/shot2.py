from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b=p.chromium.launch(); pg=b.new_page(device_scale_factor=2)
    for n in ['download2','install2']:
        pg.goto(f'file:///workspace/work/mock/{n}.html'); pg.wait_for_timeout(400)
        pg.locator('.win').screenshot(path=f'/workspace/work/mock/{n}.png')
    b.close()
