#!/usr/bin/env python3
"""Renders the marketing pages' 1200x630 share images with headless Chrome.

    python3 tools/og/generate.py            # every page in pages.json
    python3 tools/og/generate.py pricing    # one page

Writes RestaurantMenu/wwwroot/img/og/{key}-{lang}.png. Run it again after changing a page's
title in pages.json, and commit the PNGs (they are served as static files).
"""
import json, os, pathlib, subprocess, sys, time, urllib.parse, tempfile, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "RestaurantMenu", "wwwroot", "img", "og")
CHROME = os.environ.get("CHROME", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome")

def render(key, lang, page):
    query = urllib.parse.urlencode({"title": page["title"][lang], "subtitle": page["subtitle"][lang],
                                    "pill": page["pill"][lang], "photo": page.get("photo", "hero-table.webp"), "lang": lang})
    out = os.path.abspath(os.path.join(OUT, f"{key}-{lang}.png"))
    profile = tempfile.mkdtemp()
    # Chrome is given a copy in a plain temp folder: the repo path has spaces and an apostrophe.
    work = tempfile.mkdtemp()
    shutil.copy(os.path.join(HERE, "template.html"), work)
    photo = page.get("photo", "hero-table.webp")
    shutil.copy(os.path.join(HERE, "..", "..", "RestaurantMenu", "wwwroot", "img", photo), work)
    url = pathlib.Path(work, "template.html").as_uri() + "?" + query
    shot = os.path.join(work, "shot.png")
    try:
        # Headless Chrome writes the screenshot but on macOS often doesn't exit afterwards:
        # wait for the file, then stop it.
        proc = subprocess.Popen([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
                                 f"--user-data-dir={profile}", "--window-size=1200,630", f"--screenshot={shot}", url],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(600):
            if os.path.exists(shot) and os.path.getsize(shot) > 0:
                time.sleep(0.5)
                break
            time.sleep(0.1)
        proc.kill()
        if not os.path.exists(shot):
            sys.exit(f"Chrome made no screenshot for {key}-{lang}")
        shutil.copy(shot, out)
    finally:
        shutil.rmtree(profile, ignore_errors=True)
        shutil.rmtree(work, ignore_errors=True)
    print("wrote", os.path.relpath(out))

def main():
    pages = json.load(open(os.path.join(HERE, "pages.json"), encoding="utf-8"))
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[1:]
    for key, page in pages.items():
        if only and key not in only:
            continue
        for lang in page["title"]:
            render(key, lang, page)

if __name__ == "__main__":
    main()
