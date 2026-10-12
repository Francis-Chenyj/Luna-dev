"""Compile the curated search data; shipped search has no Python dependency.

To refresh official labels, pass paths to Riot Data Dragon champion.json files
using --english and --traditional. Running without these arguments regenerates
web/aliases.js from the checked-in data/search-aliases.json.
"""
import argparse
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--english", type=Path)
parser.add_argument("--traditional", type=Path)
args = parser.parse_args()
data_path = root / "data/search-aliases.json"
if args.english and args.traditional:
    catalog = json.loads((root / "data/catalog.json").read_text(encoding="utf-8-sig"))["data"]
    english = json.loads(args.english.read_text(encoding="utf-8-sig"))
    traditional = json.loads(args.traditional.read_text(encoding="utf-8-sig"))
    en = {int(h["key"]): h for h in english["data"].values()}
    tw = {int(h["key"]): h for h in traditional["data"].values()}
    community = json.loads((root / "data/community-aliases.json").read_text(encoding="utf-8"))
    entries = {}
    for hero in catalog["heroes"]:
        key = str(hero["id"])
        assert hero["id"] in en and hero["id"] in tw and key in community, key
        e, t = en[hero["id"]], tw[hero["id"]]
        entries[key] = dict(englishName=e["name"], englishTitle=e["title"],
                            traditionalName=t["name"], traditionalTitle=t["title"],
                            aliases=list(dict.fromkeys(community[key])))
    assert set(entries) == set(community)
    data = dict(schema=1, reviewedAt="2026-10-11", officialVersion=english["version"],
                sources=[
                    "https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js",
                    "https://ddragon.leagueoflegends.com/cdn/16.20.1/data/en_US/champion.json",
                    "https://ddragon.leagueoflegends.com/cdn/16.20.1/data/zh_TW/champion.json",
                    "https://www.9game.cn/news/9564777.html",
                    "https://www.bulexiu.com/n/5449.html",
                    "https://www.ptt.cc/bbs/LoL/M.1421032146.A.E51.html",
                    "https://www.sohu.com/a/433987059_120237550",
                    "https://moegirl.uk/%E5%BD%97",
                    "https://www.bilibili.com/video/BV1zikzBJE9p/"
                ], entries=entries)
    data_path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
else:
    data = json.loads(data_path.read_text(encoding="utf-8"))
encoded = json.dumps(data, ensure_ascii=False, separators=(",", ":"))
(root / "web/aliases.js").write_text(
    "// Generated from data/search-aliases.json. Names/aliases are factual lookup data.\n"
    "(function(root,data){if(typeof module==='object'&&module.exports)module.exports=data;"
    "else root.LunaAliases=data;})(typeof globalThis!=='undefined'?globalThis:this,"
    + encoded + ");\n", encoding="utf-8")
print(json.dumps({"heroes": len(data["entries"]), "curatedTerms": sum(len(e["aliases"]) for e in data["entries"].values()),
                  "officialLabels": len(data["entries"]) * 4, "scriptBytes": (root / "web/aliases.js").stat().st_size}))
