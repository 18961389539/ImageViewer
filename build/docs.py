#!/usr/bin/env python3
"""把 docs/usage.md 生成为 docs/usage.html，并检查手册覆盖度。

设计约定（改手册前先读这段）：

* 单一源是 ``docs/usage.md``，HTML 永远由它生成，不要手改 ``docs/usage.html``。
* 版式在 ``docs/usage.template.html``，脚本只填 ``{{TOC}}`` 与 ``{{CONTENT}}``。
* 支持的 Markdown 子集（够用即可，不引入第三方依赖）：
  ``#`` 标题、``##`` 章节、``###`` 小节、段落、``-`` / ``1.`` 列表、
  管道表格、围栏代码块、``>`` 提示块、行内 ``**加粗**`` / ``` `代码` ``` / ``[文字](链接)``；
  另外支持三种行内指令：
  ``<a id="anchor"></a>`` 给下一个标题指定锚点，
  ``<!-- steps -->`` 让紧随其后的有序列表变成带序号圆点的时间线样式。
* ``--check`` 模式不写文件：确认 HTML 与 md 同步，并检查 31 个 ROI 工具、
  18 个高级测量项和顶层菜单分组都在手册里出现过。CI 跑这个模式，防止文档再漂移。

用法::

    python build/docs.py            # 生成 docs/usage.html
    python build/docs.py --check    # 校验同步 + 覆盖度
"""

from __future__ import annotations

import html
import re
import sys
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
MD_PATH = DOCS / "usage.md"
HTML_PATH = DOCS / "usage.html"
TEMPLATE_PATH = DOCS / "usage.template.html"
UITEXT_PATH = ROOT / "ImageViewerControl" / "Resources" / "UiText.resx"
PLUGIN_DIR = ROOT / "ImageViewerControl" / "Plugins"

MENU_GROUPS = [
    "MenuFileAndProjects",
    "MenuDraw",
    "MenuMeasure",
    "MenuRecipe",
    "MenuViewControl",
    "MenuImageDisplay",
    "MenuAnalysisAndExport",
    "MenuPerformance",
]


class DocError(RuntimeError):
    """手写内容或仓库结构不满足生成前提时抛出。"""


# --------------------------------------------------------------------------- 行内


MODIFIERS = ("Ctrl", "Shift", "Alt")
# 单独出现也明确是"按一个键"的名字；数字键不在此列，因为正文里的 `0`、`1` 多是数值。
SINGLE_KEYS = {
    "Ctrl", "Shift", "Alt",
    "Esc", "Enter", "Delete", "Home", "End", "Tab", "PgUp", "PgDn",
    "↑", "↓", "←", "→",
}


def is_shortcut(text: str) -> bool:
    """判断行内代码是不是按键：`Ctrl+S`、`Ctrl+Shift+R`、`Ctrl++`、`↑`。

    Chinese: 只把确实表示按键的写法转成 <kbd>；`Ctrl+滚轮`、`px`、`.ivsession`
    这类含中文或属于数值的内容继续用 <code>，避免整篇都变成键帽。
    """
    if text in SINGLE_KEYS:
        return True
    if not any(text.startswith(modifier + "+") for modifier in MODIFIERS):
        return False
    # `Ctrl++` 表示"Ctrl 加加号"：去掉末尾的 "+" 后只剩修饰键本身。
    if text.endswith("++"):
        return text[:-2] in MODIFIERS

    body = text
    parts = body.split("+")
    if len(parts) < 2:
        return False
    return all(
        part and len(part) <= 12 and part.isascii() and " " not in part for part in parts
    )


def render_key(text: str) -> str:
    if text.endswith("++"):
        body, extra = text[:-2], ["+"]
    else:
        body, extra = text, []
    keys = body.split("+") + extra
    return "+".join(f"<kbd>{html.escape(key, quote=False)}</kbd>" for key in keys)


def render_inline(text: str) -> str:
    """渲染行内语法。先切出行内代码，避免代码里的星号被当成加粗。"""
    out: list[str] = []
    for part in re.split(r"(`[^`]+`)", text):
        if len(part) > 2 and part.startswith("`") and part.endswith("`"):
            content = part[1:-1]
            if is_shortcut(content):
                out.append(render_key(content))
            else:
                out.append("<code>" + html.escape(content, quote=False) + "</code>")
            continue
        escaped = html.escape(part, quote=False)
        escaped = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", escaped)
        escaped = re.sub(
            r"\[([^\]]+)\]\(([^)\s]+)\)", r'<a href="\2">\1</a>', escaped
        )
        out.append(escaped)
    return "".join(out)


# --------------------------------------------------------------------------- 块


def split_cells(row: str) -> list[str]:
    row = row.strip()
    if row.startswith("|"):
        row = row[1:]
    if row.endswith("|"):
        row = row[:-1]
    return [cell.strip() for cell in row.split("|")]


def is_separator(row: str) -> bool:
    return bool(re.fullmatch(r"\|?[\s:|-]+\|?", row.strip())) and "-" in row


def render_table(rows: list[str], section_title: str, indent: str) -> str:
    header = split_cells(rows[0])
    body = [split_cells(row) for row in rows[2:]]
    wide = ' class="wide"' if len(header) >= 5 else ""
    lines = [
        f'{indent}<div class="table-scroll" tabindex="0" role="region" aria-label="{html.escape(section_title.rstrip("：:"), quote=True)}：表格">',
        f"{indent}<table{wide}>",
        f"{indent}<thead>",
        f"{indent}<tr>",
    ]
    for cell in header:
        lines.append(f'{indent}<th scope="col">{render_inline(cell)}</th>')
    lines += [f"{indent}</tr>", f"{indent}</thead>", f"{indent}<tbody>"]
    for row in body:
        if not any(cell for cell in row):
            continue
        lines.append(f"{indent}<tr>")
        for cell in row:
            lines.append(f"{indent}<td>{render_inline(cell)}</td>")
        lines.append(f"{indent}</tr>")
    lines += [f"{indent}</tbody>", f"{indent}</table>", f"{indent}</div>"]
    return "\n".join(lines)


def render_list(items: list[tuple[bool, str]], ordered_class: str, indent: str) -> str:
    tag = "ol" if all(ordered for ordered, _ in items) else "ul"
    open_tag = f'<ol class="{ordered_class}">' if tag == "ol" and ordered_class else f"<{tag}>"
    lines = [f"{indent}{open_tag}"]
    for _, text in items:
        lines.append(f"{indent}<li>{render_inline(text)}</li>")
    lines.append(f"{indent}</{tag}>")
    return "\n".join(lines)


def slug(text: str) -> str:
    return re.sub(r"[^0-9a-zA-Z]+", "-", text).strip("-").lower() or "section"


def convert(md_text: str) -> tuple[str, str, list[tuple[str, str]]]:
    """返回 (标题, 正文 HTML, 目录项)。"""
    lines = md_text.splitlines()
    title = ""
    body: list[str] = []
    toc: list[tuple[str, str]] = []
    pending_id: str | None = None
    pending_steps = False
    section_open = False
    section_title = "手册"
    level_three_open = False
    index = 0

    def close_section() -> None:
        nonlocal section_open, level_three_open
        if level_three_open:
            body.append("      </section>")
            level_three_open = False
        if section_open:
            body.append("    </section>")
            section_open = False

    while index < len(lines):
        line = lines[index]
        stripped = line.strip()

        if not stripped:
            index += 1
            continue

        if stripped.startswith("```"):
            language = stripped[3:].strip()
            index += 1
            code: list[str] = []
            while index < len(lines) and not lines[index].strip().startswith("```"):
                code.append(lines[index])
                index += 1
            index += 1
            class_attr = f' class="language-{html.escape(language, quote=True)}"' if language else ""
            lang_attr = f' data-lang="{html.escape(language, quote=True)}"' if language else ""
            body.append(
                f"      <pre{lang_attr}><code"
                + class_attr
                + ">"
                + html.escape("\n".join(code), quote=False)
                + "</code></pre>"
            )
            continue

        anchor = re.fullmatch(r'<a id="([^"]+)"></a>', stripped)
        if anchor:
            pending_id = anchor.group(1)
            index += 1
            continue

        if re.fullmatch(r"<!--\s*steps\s*-->", stripped):
            pending_steps = True
            index += 1
            continue

        if stripped.startswith("# "):
            title = stripped[2:].strip()
            index += 1
            continue

        if stripped.startswith("## "):
            close_section()
            heading = stripped[3:].strip()
            anchor_id = pending_id or slug(heading)
            pending_id = None
            section_title = heading
            toc.append((anchor_id, heading))
            body.append(f'    <section id="{anchor_id}">')
            body.append(
                f'      <h2><a class="anchor" href="#{anchor_id}" aria-label="链接到本节">#</a>'
                f"{render_inline(heading)}</h2>"
            )
            section_open = True
            index += 1
            continue

        if stripped.startswith("### "):
            heading = stripped[4:].strip()
            if pending_id:
                body.append(f'      <h3 id="{pending_id}">{render_inline(heading)}</h3>')
                pending_id = None
            else:
                body.append(f"      <h3>{render_inline(heading)}</h3>")
            index += 1
            continue

        if stripped.startswith("|"):
            rows: list[str] = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                rows.append(lines[index])
                index += 1
            if len(rows) < 2 or not is_separator(rows[1]):
                raise DocError(f"表格缺少分隔行：{rows[0].strip()}")
            body.append(render_table(rows, section_title, "        "))
            continue

        if stripped.startswith("> "):
            quoted: list[str] = []
            while index < len(lines) and lines[index].strip().startswith(">"):
                quoted.append(render_inline(lines[index].strip().lstrip(">").strip()))
                index += 1
            lines_html = "\n".join(
                f'          <span class="meta-line">{text}</span>' for text in quoted
            )
            body.append('        <div class="meta">\n' + lines_html + "\n        </div>")
            continue

        list_match = re.match(r"^(\d+\.|[-*])\s+(.*)$", stripped)
        if list_match:
            items: list[tuple[bool, str]] = []
            while index < len(lines):
                current = lines[index].strip()
                item = re.match(r"^(\d+\.|[-*])\s+(.*)$", current)
                if not item:
                    break
                items.append((item.group(1).endswith("."), item.group(2)))
                index += 1
            ordered_class = "step-list" if pending_steps and items[0][0] else ""
            pending_steps = False
            body.append(render_list(items, ordered_class, "        "))
            continue

        paragraph: list[str] = []
        while index < len(lines):
            current = lines[index].strip()
            if (
                not current
                or current.startswith("#")
                or current.startswith("|")
                or current.startswith(">")
                or current.startswith("```")
                or re.match(r"^(\d+\.|[-*])\s+", current)
                or re.fullmatch(r'<a id="[^"]+"></a>', current)
                or re.fullmatch(r"<!--\s*steps\s*-->", current)
            ):
                break
            paragraph.append(current)
            index += 1
        body.append("        <p>" + " ".join(render_inline(text) for text in paragraph) + "</p>")

    close_section()
    return title, "\n".join(body), toc


# --------------------------------------------------------------------------- 组装


def build_html() -> str:
    md_text = MD_PATH.read_text(encoding="utf-8")
    template = TEMPLATE_PATH.read_text(encoding="utf-8")
    title, content, toc = convert(md_text)

    template_title = re.search(r"<h1>(.*?)</h1>", template)
    if not template_title:
        raise DocError("模板里找不到 <h1>，无法核对标题")
    if template_title.group(1).strip() != title:
        raise DocError(
            f"md 标题“{title}”与模板 h1“{template_title.group(1).strip()}”不一致"
        )

    toc_html = "\n".join(
        f'      <a href="#{anchor}"><span class="toc-num">{number:02d}</span>{render_inline(text)}</a>'
        for number, (anchor, text) in enumerate(toc, start=1)
    )
    # 文末再给一份"全部章节"索引：滚到底部想跳回某节时，不用先滚回顶部或用浏览器后退。
    index_html = ""
    if toc:
        index_items = "\n".join(
            f'          <li><a href="#{anchor}">{render_inline(text)}</a></li>'
            for anchor, text in toc
        )
        index_html = (
            '\n      <nav class="chapter-index" aria-label="全部章节">\n'
            '        <p class="chapter-index-title">全部章节</p>\n'
            f"        <ol>\n{index_items}\n        </ol>\n"
            "      </nav>"
        )
    return (
        template.replace("<!--{{TOC}}-->", toc_html)
        .replace("<!--{{CONTENT}}-->", content + index_html)
        .replace("\r\n", "\n")
    )


# --------------------------------------------------------------------------- 覆盖度


def load_strings() -> dict[str, str]:
    tree = ElementTree.parse(UITEXT_PATH)
    strings: dict[str, str] = {}
    for node in tree.getroot().findall("data"):
        name = node.get("name")
        value = node.find("value")
        if name and value is not None and value.text:
            strings[name] = value.text.strip()
    return strings


def collect_tools() -> list[tuple[str, bool]]:
    """从插件目录读出 (显示名资源键, 是否测量工具)。"""
    tools: list[tuple[str, bool]] = []
    for path in sorted(PLUGIN_DIR.glob("RoiPluginRegistry.*Catalog.cs")):
        for line in path.read_text(encoding="utf-8").splitlines():
            match = re.search(r'new RoiToolDescriptor\(UiText\.Get\("(\w+)"\)', line)
            if match:
                tools.append((match.group(1), "isMeasurement: true" in line))
    return tools


def collect_advanced() -> list[str]:
    kinds: list[str] = []
    for path in ROOT.glob("ImageViewerControl/**/*.cs"):
        text = path.read_text(encoding="utf-8")
        match = re.search(r"enum AdvancedMeasurementKind\s*\{(.*?)\}", text, re.S)
        if match:
            for name in match.group(1).split(","):
                name = name.strip()
                if name and not name.startswith("//"):
                    kinds.append(name)
            break
    return kinds


def check_coverage(md_text: str, strings: dict[str, str], sections: set[str]) -> list[str]:
    problems: list[str] = []
    tools = collect_tools()
    drawing = [key for key, measurement in tools if not measurement]
    measuring = [key for key, measurement in tools if measurement]
    if (len(drawing), len(measuring)) != (10, 21):
        problems.append(
            f"工具目录数量变了：绘制 {len(drawing)} 个、测量 {len(measuring)} 个（手册按 10/21 描述）"
        )
    for key in drawing + measuring:
        name = strings.get(key)
        if not name:
            problems.append(f"UiText 缺少 {key} 的文案")
            continue
        if name not in md_text:
            problems.append(f"手册没写到工具「{name}」（{key}）")

    for kind in collect_advanced():
        key = "AdvancedMeasurementOperation" + kind
        name = strings.get(key)
        if name and name not in md_text:
            problems.append(f"手册没写到高级测量「{name}」（{key}）")

    for key in MENU_GROUPS:
        name = strings.get(key)
        if name and name not in md_text:
            problems.append(f"手册没写到菜单分组「{name}」（{key}）")

    for anchor in re.findall(r'<a id="([^"]+)"></a>', md_text):
        if anchor in sections:
            continue  # 章节锚点由自动生成的目录链接，不必在正文里再写一遍
        if f"(#{anchor})" not in md_text:
            problems.append(f"锚点 #{anchor} 没有任何链接指向它")

    for target in re.findall(r"\]\((?!https?://)([^)#]+)", md_text):
        if not (DOCS / target).exists():
            problems.append(f"链接指向的文件不存在：docs/{target}")
    return problems


# --------------------------------------------------------------------------- 入口


def main(argv: list[str]) -> int:
    check_only = "--check" in argv
    html_text = build_html()
    md_text = MD_PATH.read_text(encoding="utf-8")
    sections = {anchor for anchor, _ in convert(md_text)[2]}
    problems = check_coverage(md_text, load_strings(), sections)

    if check_only:
        current = HTML_PATH.read_text(encoding="utf-8").replace("\r\n", "\n")
        if current != html_text:
            problems.append("docs/usage.html 与 usage.md 不同步，运行 python build/docs.py 重新生成")
    else:
        HTML_PATH.write_text(html_text, encoding="utf-8", newline="\n")
        print(f"已生成 {HTML_PATH.relative_to(ROOT)}（{len(html_text)} 字符）")

    if problems:
        print("手册检查未通过：", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    print("手册检查通过：HTML 与 md 同步，工具与菜单覆盖完整")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
