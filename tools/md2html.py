#!/usr/bin/env python3
"""把 Markdown 轉成自包樣式、手機友善的單一 HTML 檔(stdlib only、零相依)。
用法:python tools/md2html.py <input.md> [output.html]
支援:# 標題、| 表格 |、**粗體**、*斜體*、`行內碼`、```碼塊```、> 引用、--- 分隔線、- / 1. 清單。
給「丟給朋友看」用:輸出檔雙擊用瀏覽器開即可,瀏覽器再 Ctrl+P → 另存 PDF 也行。"""
import html
import re
import sys

CSS = """
:root{--fg:#1a1f2e;--muted:#5b6478;--bd:#e3e7ef;--accent:#2563eb;--bg:#ffffff;--code:#f3f5f9;--quote:#f0f6ff}
*{box-sizing:border-box}
body{margin:0;background:#f5f7fb;color:var(--fg);
  font-family:-apple-system,BlinkMacSystemFont,"Segoe UI","PingFang TC","Microsoft JhengHei",system-ui,sans-serif;
  line-height:1.75;font-size:17px}
.wrap{max-width:820px;margin:0 auto;background:var(--bg);padding:48px 56px;
  box-shadow:0 1px 24px rgba(0,0,0,.06);min-height:100vh}
h1{font-size:30px;line-height:1.3;margin:.2em 0 .6em;border-bottom:3px solid var(--accent);padding-bottom:.3em}
h2{font-size:23px;margin:1.6em 0 .5em;color:#0f1c3a;border-bottom:1px solid var(--bd);padding-bottom:.25em}
h3{font-size:19px;margin:1.3em 0 .4em;color:#243049}
p{margin:.7em 0}
strong{color:#0b1220;font-weight:700}
code{background:var(--code);padding:2px 6px;border-radius:5px;font-size:.9em;
  font-family:"SF Mono",Consolas,"Cascadia Code",monospace;color:#b4255e}
pre{background:#0f1729;color:#e6edf7;padding:16px 18px;border-radius:10px;overflow-x:auto;font-size:14px;line-height:1.55}
pre code{background:none;color:inherit;padding:0;font-size:13.5px}
blockquote{margin:1em 0;padding:12px 18px;background:var(--quote);border-left:4px solid var(--accent);
  border-radius:0 8px 8px 0;color:#274060}
blockquote p{margin:.3em 0}
hr{border:0;border-top:1px solid var(--bd);margin:2em 0}
table{border-collapse:collapse;width:100%;margin:1.1em 0;font-size:15.5px;display:block;overflow-x:auto}
th,td{border:1px solid var(--bd);padding:9px 12px;text-align:left;vertical-align:top}
th{background:#eef3fb;font-weight:700;color:#0f1c3a}
tr:nth-child(even) td{background:#fafbfe}
ul,ol{margin:.6em 0;padding-left:1.6em}
li{margin:.3em 0}
a{color:var(--accent)}
@media(max-width:640px){.wrap{padding:24px 18px}body{font-size:16px}h1{font-size:25px}h2{font-size:20px}}
@media print{body{background:#fff}.wrap{box-shadow:none;max-width:100%;padding:0}}
"""


def inline(t):
    # 先 escape,再用 placeholder 保護行內碼,接著粗體/斜體/連結
    t = html.escape(t)
    codes = []
    def stash(m):
        codes.append(m.group(1))
        return f"\x00{len(codes)-1}\x00"
    t = re.sub(r"`([^`]+)`", stash, t)
    t = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', t)
    t = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", t)
    t = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<em>\1</em>", t)
    t = re.sub(r"\x00(\d+)\x00", lambda m: f"<code>{codes[int(m.group(1))]}</code>", t)
    return t


def convert(md):
    lines = md.split("\n")
    out, i, n = [], 0, len(lines)
    while i < n:
        ln = lines[i]
        # 碼塊
        if ln.startswith("```"):
            buf = []
            i += 1
            while i < n and not lines[i].startswith("```"):
                buf.append(html.escape(lines[i])); i += 1
            i += 1
            out.append("<pre><code>" + "\n".join(buf) + "</code></pre>")
            continue
        # 表格(本行 | 開頭 + 下一行是分隔線)
        if ln.lstrip().startswith("|") and i + 1 < n and re.match(r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]):
            def cells(row):
                return [c.strip() for c in row.strip().strip("|").split("|")]
            head = cells(ln)
            i += 2
            rows = []
            while i < n and lines[i].lstrip().startswith("|"):
                rows.append(cells(lines[i])); i += 1
            t = ["<table><thead><tr>"] + [f"<th>{inline(h)}</th>" for h in head] + ["</tr></thead><tbody>"]
            for r in rows:
                t.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            t.append("</tbody></table>")
            out.append("".join(t))
            continue
        # 標題
        m = re.match(r"^(#{1,6})\s+(.*)$", ln)
        if m:
            lvl = len(m.group(1))
            out.append(f"<h{lvl}>{inline(m.group(2))}</h{lvl}>")
            i += 1
            continue
        # 分隔線
        if re.match(r"^\s*(---|\*\*\*|___)\s*$", ln):
            out.append("<hr>"); i += 1; continue
        # 引用
        if ln.lstrip().startswith(">"):
            buf = []
            while i < n and lines[i].lstrip().startswith(">"):
                buf.append(inline(re.sub(r"^\s*>\s?", "", lines[i]))); i += 1
            out.append("<blockquote><p>" + "<br>".join(buf) + "</p></blockquote>")
            continue
        # 清單(連續的 - / * / 數字.)
        if re.match(r"^\s*([-*]|\d+\.)\s+", ln):
            ordered = bool(re.match(r"^\s*\d+\.\s+", ln))
            tag = "ol" if ordered else "ul"
            buf = []
            while i < n and re.match(r"^\s*([-*]|\d+\.)\s+", lines[i]):
                item = re.sub(r"^\s*([-*]|\d+\.)\s+", "", lines[i])
                buf.append(f"<li>{inline(item)}</li>"); i += 1
            out.append(f"<{tag}>" + "".join(buf) + f"</{tag}>")
            continue
        # 空行
        if ln.strip() == "":
            i += 1; continue
        # 段落(吃到空行/區塊邊界)
        buf = [ln]
        i += 1
        while i < n and lines[i].strip() != "" and not re.match(r"^(#{1,6}\s|```|\s*>|\s*([-*]|\d+\.)\s|\s*(---|\*\*\*|___)\s*$)", lines[i]) and not (lines[i].lstrip().startswith("|")):
            buf.append(lines[i]); i += 1
        out.append("<p>" + "<br>".join(inline(b) for b in buf) + "</p>")
    return "\n".join(out)


def main():
    if len(sys.argv) < 2:
        print("用法: python tools/md2html.py <input.md> [output.html]"); sys.exit(1)
    inp = sys.argv[1]
    outp = sys.argv[2] if len(sys.argv) > 2 else re.sub(r"\.md$", "", inp) + ".html"
    md = open(inp, encoding="utf-8").read()
    title = "說明"
    m = re.search(r"^#\s+(.*)$", md, re.M)
    if m:
        title = m.group(1)
    body = convert(md)
    doc = ("<!DOCTYPE html><html lang=\"zh-Hant\"><head><meta charset=\"UTF-8\">"
           "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">"
           f"<title>{html.escape(title)}</title><style>{CSS}</style></head>"
           f"<body><div class=\"wrap\">{body}</div></body></html>")
    open(outp, "w", encoding="utf-8").write(doc)
    print(f"OK → {outp}")


if __name__ == "__main__":
    main()
