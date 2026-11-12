"""One-off: the 2026-11-09 theme roster (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md, slice 3).

Adds the missing light/dark half to existing families and writes the new families. A family that had one look keeps it as its FIRST variant (an
empty variant over the unchanged base), so a saved bare key still resolves to it and its golden snapshot still holds. Run once from the repo root:

    py -I tools/themes/build_roster_2026_11.py

Kept in the repo as the record of every value; re-running rewrites the same files.
"""
import json
import os
from collections import OrderedDict

SKINS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "Paperbunkr.App", "Assets", "Skins")

LIGHT_STATUS = {"danger": "#B3261E", "warning": "#8A5A00"}


def o(**kw):
    return OrderedDict(kw)


def tex(source, fallback):
    return o(type="texture", source=source, fallback=fallback)


def img(source, fallback):
    return o(type="image", source=source, fallback=fallback, stretch="uniformToFill")


def grad(angle, top, bottom, fallback):
    return o(type="linearGradient", angle=angle, fallback=fallback, stops=[o(at=0, color=top), o(at=1, color=bottom)])


def load(key):
    with open(os.path.join(SKINS, key, "theme.json"), encoding="utf-8-sig") as f:
        return json.load(f, object_pairs_hook=OrderedDict)


def save(key, doc):
    os.makedirs(os.path.join(SKINS, key), exist_ok=True)
    with open(os.path.join(SKINS, key, "theme.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("wrote", key)


def add_variant(key, variant_id, variant, first_id=None, first_name=None, first_extra=None):
    """Adds a variant. A single-look family first becomes a family whose first variant is its old look (empty, plus first_extra)."""
    doc = load(key)
    if "variants" not in doc:
        mode = doc.pop("mode")
        first = o(name=first_name, mode=mode)
        if first_extra:
            first.update(first_extra)
        rebuilt = OrderedDict()
        for k, v in doc.items():
            rebuilt[k] = v
        rebuilt["variants"] = OrderedDict([(first_id, first)])
        doc = rebuilt
    doc["variants"][variant_id] = variant
    save(key, doc)


def full_semantic(canvas, nav, recessed, default, raised, overlay, border, text, text2, text3, accent_text, accent, accent_soft, badge, badge_text,
                  success, chart1, chart2, glow, hero_start, hero_end, status=None):
    """Every semantic colour a converted v1 base sets, so a new half replaces all of them."""
    s = o(surface=o(canvas=canvas, navigation=nav, recessed=recessed, default=default, raised=raised, overlay=overlay),
          border=o(default=border),
          text=o(primary=text, secondary=text2, muted=text3, accent=accent_text),
          action=o(primary=accent, soft=accent_soft),
          badge=o(background=badge, text=badge_text),
          status=o(success=success),
          chart=o(series=OrderedDict([("1", chart1), ("2", chart2)])),
          glow=o(color=glow),
          hero=o(gradient=o(start=hero_start, end=hero_end)))
    if status:
        s["status"].update(status)
    return s


# ======================================================================================================== partners of existing families

def partners():
    add_variant("abyss", "shallows", o(
        name="Shallows", mode="Light",
        palette=o(canvas="#E6F6FB", ink="#0B2A3C", accent="#086A96", success="#1E7A5A", badge="#B07A00"),
        materials=o(water=img("textures/shallows.png", "#E6F6FB"), reef=grad(180, "#D3EDF6", "#BFE3EF", "#CDEAF4"),
                    shelfLight=grad(135, "#F4FBFD", "#E0F2F8", "#EEF8FB")),
        surfaces=o(canvas="water", navigation="reef", raised="shelfLight"),
        semantic=o(surface=o(raised="#F2FAFC", overlay="#FFFFFF"), border=o(default="#A9D3E3"),
                   status=o(danger="#B3263A", warning="#8A5A00", info="#0A6F9C"),
                   chart=o(series=OrderedDict([("1", "#086A96"), ("2", "#6C5BD4")])), glow=o(color="#66086A96")),
        components=o(**{"button.primary": o(foreground="#FFFFFF")}),
    ))

    add_variant("tokyo_midnight", "daybreak", o(
        name="Daybreak", mode="Light",
        palette=o(canvas="#FCEFF6", ink="#2A1838", accent="#C21E6C", success="#1E8A64", badge="#00788A"),
        materials=o(dawn=o(type="linearGradient", angle=180, fallback="#FBEFF6",
                           stops=[o(at=0, color="#FFE9F2"), o(at=0.6, color="#F3E8FF"), o(at=1, color="#E6F7FB")]),
                    dawnNav=grad(180, "#FBDDEB", "#E9DDFB", "#F2DDF2")),
        surfaces=o(canvas="dawn", navigation="dawnNav"),
        semantic=o(surface=o(raised="#FFF8FB", overlay="#FFFFFF"), border=o(default="#EBC6DA"),
                   status=o(danger="#C0243A", warning="#8A5A00", info="#00788A"),
                   chart=o(series=OrderedDict([("1", "#00869A"), ("2", "#7B4FD6")])),
                   glow=o(color="#66C21E6C"), selection=o(background="#33C21E6C")),
        typography=o(heading=o(family="Bahnschrift"), display=o(family="Bahnschrift")),
        components=o(**{"button.primary": o(foreground="#FFFFFF", states=o(hover=o(background="#D63A82")))}),
    ))

    add_variant("arcade", "pastel", o(
        name="Pastel", mode="Light",
        palette=o(canvas="#F4EEFC", ink="#2B1650", accent="#BA2283", success="#0E8A6A", badge="#8A6A00"),
        materials=o(pastelGrid=img("textures/pastel.png", "#F2EEFC"), pastelNav=grad(180, "#EBDDFF", "#DDF6F1", "#E6E6F8"),
                    pastelCabinet=grad(135, "#FBF7FF", "#F1ECFB", "#F6F2FD")),
        surfaces=o(canvas="pastelGrid", navigation="pastelNav", raised="pastelCabinet"),
        semantic=o(surface=o(raised="#F8F4FE", overlay="#FFFFFF"), border=o(default="#D9B8F5", focus="#0088A8"),
                   status=o(danger="#C0244E", warning="#8A5A00", info="#0088A8"),
                   chart=o(series=OrderedDict([("1", "#0088A8"), ("2", "#8A3FD6")])), glow=o(color="#66C8288A")),
        typography=o(heading=o(family="Bahnschrift"), display=o(family="Bahnschrift")),
        components=o(**{"button.primary": o(foreground="#FFFFFF", borderThickness=2, states=o(hover=o(background="#DC3C9E"))),
                        "button.secondary": o(borderThickness=2), "button.ghost": o(borderThickness=2)}),
    ), first_id="dark", first_name="Dark")

    # Crimson Ink: the dark look gains a fine ink grain (an intended change); its light half is editorial red-and-black on bright stock.
    add_variant("crimson_ink", "paper", o(
        name="Paper", mode="Light",
        palette=o(canvas="#FAF8F5", ink="#141414", accent="#C8102E", success="#2E7A46", badge="#141414"),
        materials=o(paperGrain=tex("textures/paper-grain.png", "#FAF8F5")),
        surfaces=o(canvas="paperGrain"),
        semantic=o(surface=o(navigation="#F1EEEA", raised="#FFFFFF", overlay="#FFFFFF"), border=o(default="#CFC9C2", strong="#141414"),
                   badge=o(text="#FAF8F5"), status=o(danger="#B0102A", warning="#7A5A0A", info="#1F4E79"),
                   chart=o(series=OrderedDict([("1", "#C8102E"), ("2", "#5E5A55")])), glow=o(color="#40C8102E")),
        typography=o(heading=o(family="Georgia"), display=o(family="Georgia")),
        components=o(**{"button.primary": o(foreground="#FFFFFF", states=o(hover=o(background="#A80D26"))),
                        "button.secondary": o(border="#141414"), "button.ghost": o(border="#141414"), "chip": o(border="#141414")}),
    ), first_id="dark", first_name="Dark",
        first_extra=o(materials=o(inkGrain=tex("textures/ink-grain.png", "#121214")), surfaces=o(canvas="inkGrain")))

    printout = full_semantic("#FFFFFF", "#F7FAF6", "#EEF4EC", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#9DBB98", "#1C2A1C", "#3F5A3F", "#5E755C", "#1E6B25",
                             "#2E7D32", "#292E7D32", "#2E7D32", "#FFFFFF", "#2E7D32", "#2E7D32", "#1C2A1C", "#662E7D32", "#00FFFFFF", "#FFFFFFFF",
                             status=o(danger="#B3261E", warning="#7A5A00", info="#1F5FA8"))
    add_variant("matrix", "printout", o(
        name="Printout", mode="Light",
        materials=o(greenbar=tex("textures/greenbar.png", "#FFFFFF"), sprocket=tex("textures/sprocket.png", "#F7FAF6")),
        surfaces=o(canvas="greenbar", navigation="sprocket"),
        semantic=printout,
        metrics=o(radius=0, radiusSm=0, radiusLg=2),
        typography=o(heading=o(family="Consolas"), display=o(family="Consolas")),
        components=o(**{"button.primary": o(foreground="#FFFFFF")}),
    ), first_id="dark", first_name="Dark")

    add_variant("newsprint", "night", o(
        name="Night Edition", mode="Dark",
        palette=o(canvas="#141412", ink="#EDEAE2", accent="#EDEAE2", success="#7FB08A", badge="#EDEAE2"),
        materials=o(nightGrain=tex("textures/night-grain.png", "#141412")),
        surfaces=o(canvas="nightGrain"),
        semantic=o(surface=o(navigation="#1B1B18", raised="#1D1D1A", overlay="#262622"), border=o(default="#4A4842", strong="#EDEAE2"),
                   text=o(accent="#EDEAE2"), badge=o(text="#141412"), action=o(primaryText="#141412", soft="#26EDEAE2"),
                   glow=o(color="#40EDEAE2"), status=o(danger="#E26B66", warning="#D9B45A", info="#7FA7D1"),
                   chart=o(series=OrderedDict([("1", "#7FA7D1"), ("2", "#B79BD6")])),
                   reader=o(background=o(page="#1A1A17", text="#E6E2D8"))),
        components=o(**{"button.primary": o(foreground="#141412", states=o(hover=o(background="#FFFFFF")))}),
    ), first_id="paper", first_name="Paper")

    add_variant("windows_11", "dark", o(
        name="Dark", mode="Dark",
        semantic=full_semantic("#202020", "#2B2B2B", "#1C1C1C", "#202020", "#2B2B2B", "#2D2D2D", "#3A3A3A", "#FFFFFF", "#C5C5C5", "#9A9A9A", "#60CDFF",
                               "#60CDFF", "#2960CDFF", "#FFB900", "#1A1A1A", "#6CCB5F", "#60CDFF", "#9F8CFF", "#6660CDFF", "#00202020", "#FF202020"),
        components=o(**{"button.primary": o(foreground="#00263A")}),
    ), first_id="light", first_name="Light")


# ======================================================================================================== accessibility light halves

def accessibility():
    add_variant("maximum_contrast", "light", o(
        name="Light", mode="Light",
        semantic=full_semantic("#FFFFFF", "#FFFFFF", "#F2F2F2", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#000000", "#000000", "#1A1A1A", "#4D4D4D", "#0037DA",
                               "#0037DA", "#330037DA", "#0037DA", "#FFFFFF", "#0B6A0B", "#0037DA", "#8A00B8", "#800037DA", "#00FFFFFF", "#FFFFFFFF",
                               status=o(danger="#B00020", warning="#6B4A00", info="#0037DA")),
        components=o(**{"button.primary": o(foreground="#FFFFFF", borderThickness=2), "button.secondary": o(borderThickness=2),
                        "button.ghost": o(borderThickness=2), "chip": o(borderThickness=2), "panel.floating": o(borderThickness=2)}),
    ), first_id="dark", first_name="Dark")

    add_variant("colorblind_safe", "light", o(
        name="Light", mode="Light",
        semantic=full_semantic("#FFFFFF", "#F4F4F4", "#EDEDED", "#FFFFFF", "#FAFAFA", "#FFFFFF", "#CFCFCF", "#1A1A1A", "#4D4D4D", "#6B6B6B", "#005A8C",
                               "#0072B2", "#290072B2", "#E69F00", "#1A1A1A", "#007A58", "#0072B2", "#CC79A7", "#660072B2", "#00FFFFFF", "#FFFFFFFF",
                               status=o(danger="#B34700", warning="#8A5A00", info="#0072B2")),
        components=o(**{"button.primary": o(foreground="#FFFFFF")}),
    ), first_id="dark", first_name="Dark")


# ======================================================================================================== new families

def family(key, name, base, variants, mode=None):
    doc = o(schemaVersion=2, id="paperbunkr." + key, name=name)
    if mode:
        doc["mode"] = mode
    doc["base"] = base
    if variants:
        doc["variants"] = variants
    save(key, doc)


def thick(width, border=None):
    c = o()
    for role in ("button.primary", "button.secondary", "button.ghost"):
        c[role] = o(borderThickness=width)
        if border:
            c[role]["border"] = border
    c["chip"] = o(borderThickness=width)
    c["panel.floating"] = o(borderThickness=width)
    if border:
        c["chip"]["border"] = border
        c["panel.floating"]["border"] = border
    return c


def new_families():
    day_c = thick(2, "#231A10")
    day_c["button.primary"].update(foreground="#FFF4D6")
    night_c = thick(2, "#F2E6C8")
    night_c["button.primary"].update(foreground="#1A1405")
    family("golden_age", "Golden Age",
           o(metrics=o(radius=3, radiusSm=3, radiusLg=6), typography=o(heading=o(family="Impact"), display=o(family="Impact"))),
           OrderedDict([
               ("day", o(name="Day", mode="Light",
                         palette=o(canvas="#F2E6C8", ink="#231A10", accent="#C21F18", success="#2E6B3E", badge="#F5C518"),
                         materials=o(dots=tex("textures/day-canvas.png", "#F2E6C8"), navDots=tex("textures/day-nav.png", "#EADBB6")),
                         surfaces=o(canvas="dots", navigation="navDots"),
                         semantic=o(surface=o(raised="#F7EDD3", overlay="#FBF4E2"), border=o(default="#8C7A55", strong="#231A10"),
                                    badge=o(text="#231A10"), status=o(danger="#B3261E", warning="#7A5A0A", info="#1D4E9E"),
                                    chart=o(series=OrderedDict([("1", "#1D4E9E"), ("2", "#C21F18")])), glow=o(color="#66C21F18")),
                         components=day_c)),
               ("midnight", o(name="Midnight", mode="Dark",
                              palette=o(canvas="#141A30", ink="#F2E6C8", accent="#F5C518", success="#7FC48A", badge="#C42A22"),
                              materials=o(dots=tex("textures/night-canvas.png", "#141A30"), navDots=tex("textures/night-nav.png", "#111628")),
                              surfaces=o(canvas="dots", navigation="navDots"),
                              semantic=o(surface=o(raised="#1B2240", overlay="#222A4C"), border=o(default="#3A4470", strong="#F2E6C8"),
                                         badge=o(text="#FFFFFF"), status=o(danger="#FF6B5E", warning="#F5C518", info="#7FA7E8"),
                                         chart=o(series=OrderedDict([("1", "#F5C518"), ("2", "#C42A22")])), glow=o(color="#66F5C518")),
                              components=night_c)),
           ]))

    paper_c = thick(3)
    paper_c["button.primary"].update(foreground="#FFFFFF")
    ink_c = thick(3)
    ink_c["button.primary"].update(foreground="#000000")
    family("screentone", "Screentone",
           o(metrics=o(radius=2, radiusSm=2, radiusLg=3), typography=o(heading=o(family="Arial", weight="Black"), display=o(family="Arial", weight="Black"))),
           OrderedDict([
               ("paper", o(name="Paper", mode="Light",
                           palette=o(canvas="#FFFFFF", ink="#000000", accent="#000000", success="#1E6B2E", badge="#E4002B"),
                           materials=o(tone=tex("textures/light-tone.png", "#FFFFFF"), selTone=tex("textures/light-selection.png", "#EBEBEB")),
                           surfaces=o(navigation="tone", selection="selTone"),
                           semantic=o(surface=o(raised="#FFFFFF", overlay="#FFFFFF"), border=o(default="#000000", strong="#000000"),
                                      text=o(accent="#000000"), action=o(primaryText="#FFFFFF", soft="#1F000000"), badge=o(text="#FFFFFF"),
                                      status=o(danger="#E4002B", warning="#7A5A00", info="#000000"),
                                      chart=o(series=OrderedDict([("1", "#000000"), ("2", "#E4002B")])), glow=o(color="#66000000"),
                                      selection=o(background="#1F000000")),
                           components=paper_c)),
               ("ink", o(name="Ink", mode="Dark",
                         palette=o(canvas="#0A0A0A", ink="#FFFFFF", accent="#FFFFFF", success="#6FD08A", badge="#FF3355"),
                         materials=o(tone=tex("textures/dark-tone.png", "#0A0A0A"), selTone=tex("textures/dark-selection.png", "#222222")),
                         surfaces=o(navigation="tone", selection="selTone"),
                         semantic=o(surface=o(raised="#121212", overlay="#1A1A1A"), border=o(default="#FFFFFF", strong="#FFFFFF"),
                                    text=o(accent="#FFFFFF"), action=o(primaryText="#000000", soft="#26FFFFFF"), badge=o(text="#000000"),
                                    status=o(danger="#FF3355", warning="#FFD24D", info="#FFFFFF"),
                                    chart=o(series=OrderedDict([("1", "#FFFFFF"), ("2", "#FF3355")])), glow=o(color="#66FFFFFF"),
                                    selection=o(background="#26FFFFFF")),
                         components=ink_c)),
           ]))

    family("risograph", "Risograph",
           o(metrics=o(radius=6, radiusSm=4, radiusLg=12), typography=o(heading=o(family="Trebuchet MS"), display=o(family="Trebuchet MS"))),
           OrderedDict([
               ("white", o(name="White Stock", mode="Light",
                           palette=o(canvas="#F4F1EA", ink="#1F2A55", accent="#C2186F", success="#1C7A5A", badge="#006BA8"),
                           materials=o(grain=tex("textures/light-grain.png", "#F4F1EA")), surfaces=o(canvas="grain"),
                           semantic=o(surface=o(navigation="#EFEAE0", raised="#F9F7F2", overlay="#FFFFFF"), border=o(default="#D8D1C2"),
                                      status=o(danger="#C0243A", warning="#8A5A00", info="#006BA8"),
                                      chart=o(series=OrderedDict([("1", "#C2186F"), ("2", "#006BA8")])), glow=o(color="#66D61F82")),
                           components=o(**{"button.primary": o(foreground="#FFFFFF", shadow="2 2 0 0 #006BA8")}))),
               ("black", o(name="Black Stock", mode="Dark",
                           palette=o(canvas="#1A1A1C", ink="#F3EFE6", accent="#FF48B0", success="#6FD08A", badge="#FFE800"),
                           materials=o(grain=tex("textures/dark-grain.png", "#1A1A1C")), surfaces=o(canvas="grain"),
                           semantic=o(surface=o(navigation="#222225", raised="#1F1F22", overlay="#29292D"), border=o(default="#3A3A3F"),
                                      status=o(danger="#FF5C7A", warning="#FFE800", info="#4FB8FF"),
                                      chart=o(series=OrderedDict([("1", "#FF48B0"), ("2", "#FFE800")])), glow=o(color="#66FF48B0")),
                           components=o(**{"button.primary": o(foreground="#1A1A1C", shadow="2 2 0 0 #FFE800")}))),
           ]))

    family("blueprint", "Blueprint",
           o(metrics=o(radius=0, radiusSm=0, radiusLg=2), typography=o(heading=o(family="Bahnschrift"), display=o(family="Bahnschrift"))),
           OrderedDict([
               ("whiteprint", o(name="Whiteprint", mode="Light",
                                palette=o(canvas="#F5F9FD", ink="#0B3A6E", accent="#1565C0", success="#2E7D32", badge="#C2410C"),
                                materials=o(grid=tex("textures/light-grid.png", "#F5F9FD")), surfaces=o(canvas="grid"),
                                semantic=o(surface=o(navigation="#EAF2FA", raised="#FBFDFF", overlay="#FFFFFF"),
                                           border=o(default="#8DB2DA", strong="#0B3A6E"),
                                           status=o(danger="#B3261E", warning="#8A5A00", info="#1565C0"),
                                           chart=o(series=OrderedDict([("1", "#1565C0"), ("2", "#C2410C")])), glow=o(color="#661565C0")),
                                components=o(**{"button.primary": o(foreground="#FFFFFF")}))),
               ("cyanotype", o(name="Cyanotype", mode="Dark",
                               palette=o(canvas="#0F3460", ink="#E8F1FF", accent="#FFFFFF", success="#8EE3B0", badge="#FFB454"),
                               materials=o(grid=tex("textures/dark-grid.png", "#0F3460")), surfaces=o(canvas="grid"),
                               semantic=o(surface=o(navigation="#123B6B", raised="#134070", overlay="#174A80"),
                                          border=o(default="#6F95C7", strong="#FFFFFF"), text=o(accent="#FFFFFF"),
                                          action=o(soft="#26FFFFFF"), badge=o(text="#0F3460"),
                                          status=o(danger="#FF8A80", warning="#FFB454", info="#BFE0FF"),
                                          chart=o(series=OrderedDict([("1", "#FFFFFF"), ("2", "#FFB454")])), glow=o(color="#66FFFFFF")),
                               components=o(**{"button.primary": o(foreground="#0F3460")}))),
           ]))

    family("dune", "Dune",
           o(metrics=o(radius=8, radiusSm=6, radiusLg=14),
             typography=o(heading=o(family="Palatino Linotype"), display=o(family="Palatino Linotype"))),
           OrderedDict([
               ("day", o(name="Day", mode="Light",
                         palette=o(canvas="#FBF1E1", ink="#3B2414", accent="#B04E25", success="#2F6F73", badge="#D9A441"),
                         materials=o(dunes=img("textures/day.png", "#F7EAD3")), surfaces=o(canvas="dunes"),
                         semantic=o(surface=o(navigation="#F8ECD6", raised="#FCF5E9", overlay="#FFFAF2"), border=o(default="#E0C79F"),
                                    badge=o(text="#3B2414"), status=o(danger="#B3261E", warning="#8A5A00", info="#2F6F73"),
                                    chart=o(series=OrderedDict([("1", "#2F6F73"), ("2", "#B04E25")])), glow=o(color="#66B04E25")),
                         components=o(**{"button.primary": o(foreground="#FFF7EA")}))),
               ("night", o(name="Night", mode="Dark",
                           palette=o(canvas="#1B1A30", ink="#F3E3C7", accent="#E0A458", success="#8FC9A0", badge="#D2693C"),
                           materials=o(dunes=img("textures/night.png", "#221B2C")), surfaces=o(canvas="dunes"),
                           semantic=o(surface=o(navigation="#1F1C30", raised="#252036", overlay="#2E2840"), border=o(default="#3E3044"),
                                      badge=o(text="#2A1608"), status=o(danger="#F08A70", warning="#E0A458", info="#9FB8E8"),
                                      chart=o(series=OrderedDict([("1", "#E0A458"), ("2", "#D2693C")])), glow=o(color="#66E0A458")),
                           components=o(**{"button.primary": o(foreground="#2A1608")}))),
           ]))

    family("kraft", "Kraft", o(
        palette=o(canvas="#CFAE7E", ink="#2A1B0E", accent="#7E1812", success="#2E5E2E", badge="#1F3A93"),
        materials=o(fibre=tex("textures/canvas.png", "#CFAE7E"), navFibre=tex("textures/nav.png", "#C4A170")),
        surfaces=o(canvas="fibre", navigation="navFibre"),
        semantic=o(surface=o(raised="#D9BC90", overlay="#E3CBA5"), border=o(default="#A9875A", strong="#2A1B0E"),
                   text=o(secondary="#4A3520", muted="#5C452C", accent="#6E1510"), badge=o(text="#F6E7CC"),
                   status=o(danger="#7E1812", warning="#6B4A00", info="#1F3A93"),
                   chart=o(series=OrderedDict([("1", "#1F3A93"), ("2", "#7E1812")])), glow=o(color="#669E1F18")),
        metrics=o(radius=4, radiusSm=3, radiusLg=8),
        typography=o(heading=o(family="Courier New"), display=o(family="Courier New")),
        components=o(**{"button.primary": o(foreground="#F6E7CC")}),
    ), None, mode="Light")


if __name__ == "__main__":
    partners()
    accessibility()
    new_families()
