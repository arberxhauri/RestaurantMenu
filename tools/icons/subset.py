#!/usr/bin/env python3
"""Phosphor icons, cut down to the ones the app uses.

    python3 -m venv /tmp/venv && /tmp/venv/bin/pip install fonttools brotli
    /tmp/venv/bin/python tools/icons/subset.py

Scans Views, wwwroot/js and the C# sources for "ph-..." class names, then writes
RestaurantMenu/wwwroot/lib/phosphor/icons.css and icons-regular.woff2 / icons-fill.woff2 with only
those icons (a few KB instead of ~430 KB of CSS and fonts on every page). Run it again after using
an icon that isn't in the set; IconSubsetTests fails until you do.
"""
import os, re, sys
from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "RestaurantMenu")
LIB = os.path.join(ROOT, "wwwroot", "lib", "phosphor")
NAME = re.compile(r"ph-[a-z0-9]+(?:-[a-z0-9]+)*")

def used_icons():
    names = set()
    for folder in ("Views", os.path.join("wwwroot", "js"), "Helpers", "Controllers", "Services"):
        for dirpath, _, files in os.walk(os.path.join(ROOT, folder)):
            for f in files:
                if f.startswith("._") or not f.endswith((".cshtml", ".js", ".cs")):
                    continue
                names.update(NAME.findall(open(os.path.join(dirpath, f), encoding="utf-8", errors="ignore").read()))
    names.discard("ph-fill")
    return names

def rules(css_path, prefix):
    css = open(css_path, encoding="utf-8").read()
    found = {}
    for m in re.finditer(r"\.%s\.(ph-[a-z0-9-]+):before\s*{\s*content:\s*\"\\([0-9a-f]+)\";" % re.escape(prefix), css):
        found[m.group(1)] = m.group(2)
    return found

def main():
    names = used_icons()
    regular = rules(os.path.join(LIB, "regular", "style.css"), "ph")
    fill = rules(os.path.join(LIB, "fill", "style.css"), "ph-fill")
    missing = sorted(n for n in names if n not in regular and n not in fill)
    css = ["/* Phosphor Icons 2.1.1 (MIT): only the icons this app uses (tools/icons/subset.py, generated). */"]
    for family, font, table, cls, out in (("Phosphor", "regular/Phosphor.woff2", regular, ".ph", "icons-regular.woff2"),
                                          ("Phosphor-Fill", "fill/Phosphor-Fill.woff2", fill, ".ph-fill", "icons-fill.woff2")):
        codes = sorted({int(table[n], 16) for n in names if n in table})
        f = TTFont(os.path.join(LIB, font))
        options = subset.Options()
        options.flavor = "woff2"
        options.layout_features = []
        options.name_IDs = []
        options.notdef_outline = True
        sub = subset.Subsetter(options)
        sub.populate(unicodes=codes)
        sub.subset(f)
        f.flavor = "woff2"
        f.save(os.path.join(LIB, out))
        css.append('@font-face { font-family: "%s"; src: url("./%s") format("woff2"); font-weight: normal; font-style: normal; font-display: block; }' % (family, out))
        css.append('%s { font-family: "%s" !important; speak: never; font-style: normal; font-weight: normal; font-variant: normal; '
                   'text-transform: none; line-height: 1; -webkit-font-smoothing: antialiased; -moz-osx-font-smoothing: grayscale; }' % (cls, family))
        prefix = ".ph" if cls == ".ph" else ".ph-fill"
        for n in sorted(names):
            if n in table:
                css.append('%s.%s:before { content: "\\%s"; }' % (prefix, n, table[n]))
    open(os.path.join(LIB, "icons.css"), "w", encoding="utf-8").write("\n".join(css) + "\n")
    print(f"{len(names)} icon names, regular {sum(n in regular for n in names)}, fill {sum(n in fill for n in names)}")
    if missing:
        print("not Phosphor icons (ignored):", ", ".join(missing))

if __name__ == "__main__":
    main()
