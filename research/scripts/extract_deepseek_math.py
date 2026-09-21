import pypdf
import json
import re

reader = pypdf.PdfReader('research/papers/deepseek_cordis_2608.25512.pdf')

results = {
    "definitions": [],
    "theorems": [],
    "lemmas": [],
    "problems_solved": [],
    "evaluations_tests": []
}

pattern = re.compile(r'^(Definition|Theorem|Lemma|Proposition|Corollary)\s+(\d+(\.\d+)*)\.', re.MULTILINE)

full_text = []
for i, page in enumerate(reader.pages):
    txt = page.extract_text()
    full_text.append((i + 1, txt))
    matches = pattern.finditer(txt)
    for m in matches:
        start_idx = m.start()
        # grab next 400 characters
        snippet = txt[start_idx:start_idx + 600].replace('\n', ' ')
        kind = m.group(1).lower()
        num = m.group(2)
        item = {"page": i + 1, "kind": kind, "num": num, "text": snippet}
        if "definition" in kind:
            results["definitions"].append(item)
        elif "theorem" in kind:
            results["theorems"].append(item)
        elif "lemma" in kind or "proposition" in kind or "corollary" in kind:
            results["lemmas"].append(item)

# Look for Section 5 (Implementation and Case Study / Evaluation)
sec5_text = []
for page_num, txt in full_text:
    if 57 <= page_num <= 85:
        for line in txt.split('\n'):
            if any(k in line.lower() for k in ['evaluat', 'benchmark', 'overhead', 'memory', 'leak', 'performance', 'latency', 'koishi', 'case study', 'test']):
                sec5_text.append(f"Page {page_num}: {line.strip()}")

results["evaluations_tests"] = sec5_text[:50]

with open("research/papers/deepseek_formal_analysis.json", "w", encoding="utf-8") as f:
    json.dump(results, f, indent=2)

print(f"Extracted {len(results['definitions'])} definitions, {len(results['theorems'])} theorems, {len(results['lemmas'])} lemmas/propositions.")
print(f"Sampled {len(results['evaluations_tests'])} evaluation/case study lines.")
