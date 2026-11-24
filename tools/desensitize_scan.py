"""交付物脱敏扫描。

用法：
    python tools/desensitize_scan.py

敏感词从仓库根目录的 `.sensitive.local` 读取（一行一个，# 开头为注释）。
该文件只存在于本机，已加入 .gitignore；不要把真实词表提交到仓库。

扫描范围：仓库内文本类源码/文档/配置；自动跳过构建产物、.git、
source/ 与 data/（这两个目录只允许放本机真实资料）。
命中任意词条即返回退出码 1，供 CI / 提交前检查使用。
"""

from __future__ import annotations

import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SENSITIVE_FILE = os.path.join(ROOT, ".sensitive.local")

SKIP_DIRS = {
    ".git", "bin", "obj", ".vs", "node_modules", "source", "data",
    "TestResults", ".idea", ".gradle",
}
TEXT_EXT = {
    ".cs", ".xaml", ".csproj", ".props", ".sln", ".md", ".html", ".css",
    ".js", ".json", ".sql", ".py", ".txt", ".xml", ".yml", ".yaml",
    ".gitignore", ".gitmodules", ".gradle", ".kts", ".java", ".kt",
}
# 只对超长行做截断，避免把整段真实文本打印到终端
MAX_PRINT = 160


def load_terms() -> list[str]:
    if not os.path.exists(SENSITIVE_FILE):
        print("[WARN] 未找到 .sensitive.local，无法执行项目级敏感词检查。")
        print("       请在仓库根目录创建该文件（一行一个词，已 gitignore）。")
        return []
    terms: list[str] = []
    with io.open(SENSITIVE_FILE, encoding="utf-8") as f:
        for line in f:
            term = line.strip()
            if term and not term.startswith("#"):
                terms.append(term)
    # 长词优先，避免短词先命中后看不出真正问题
    terms.sort(key=len, reverse=True)
    return terms


def mask(term: str) -> str:
    if len(term) <= 2:
        return term[0] + "*"
    return term[0] + "*" * (len(term) - 1)


def iter_files():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            ext = os.path.splitext(name)[1].lower()
            if ext in TEXT_EXT or name in (".gitignore", ".gitmodules"):
                yield os.path.join(base, name)


def main() -> int:
    terms = load_terms()
    hits = 0
    for path in iter_files():
        try:
            text = io.open(path, encoding="utf-8", errors="strict").read()
        except (UnicodeDecodeError, OSError):
            continue
        for line_no, line in enumerate(text.splitlines(), 1):
            for term in terms:
                if term in line:
                    rel = os.path.relpath(path, ROOT)
                    snippet = line.strip().replace(term, mask(term))
                    if len(snippet) > MAX_PRINT:
                        snippet = snippet[:MAX_PRINT] + "…"
                    print(f"{rel}:{line_no}: {snippet}")
                    hits += 1
                    break  # 一行只报一次，避免同一处重复刷屏
    if hits:
        print(f"\n[FAIL] 命中 {hits} 处敏感词，请返工后再提交。")
        return 1
    print("[PASS] 未命中敏感词。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
