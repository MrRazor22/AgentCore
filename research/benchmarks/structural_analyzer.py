"""
Experiment 3: Structural Metrics Extraction Engine.
Extracts public type count, public method count, DIT, and CBO across all six pinned framework repositories.
"""

import ast
import os
import re
from dataclasses import dataclass, asdict
from typing import Dict, List, Set, Tuple

@dataclass
class StructuralMetrics:
    framework: str
    language: str
    scope_path: str
    public_types: int
    public_methods: int
    max_dit: int
    mean_dit: float
    mean_cbo: float
    sloc: int
    cli: float  # Conceptual Load Index: 0.4*N_type + 0.04*N_meth + 0.1*Max_DIT + 0.1*Mean_CBO

def count_csharp_metrics(directories) -> Tuple[int, int, int, float, float, int]:
    public_types = 0
    public_methods = 0
    dit_list = []
    cbo_list = []
    sloc = 0

    class_pattern = re.compile(r'\bpublic\s+(?:sealed\s+|abstract\s+|static\s+)?(?:class|interface|struct|record)\s+(\w+)(?:\s*:\s*([\w\s,<>]+))?')
    method_pattern = re.compile(r'\bpublic\s+(?:override\s+|virtual\s+|async\s+|static\s+|sealed\s+)*(?!class|interface|struct|record|enum)(?:[\w<>\[\],\?]+)\s+(\w+)\s*\(')

    dir_list = [directories] if isinstance(directories, str) else list(directories)
    for directory in dir_list:
        for root, dirs, files in os.walk(directory):
            if any(ignored in root for ignored in ['bin', 'obj', 'Tests', 'Test', 'sample', 'Sample', '.git', 'extern', '_ai_context']):
                continue
            for f in files:
                if f.endswith('.cs'):
                    filepath = os.path.join(root, f)
                with open(filepath, 'r', encoding='utf-8', errors='ignore') as fp:
                    lines = fp.readlines()
                    code_lines = [l for l in lines if l.strip() and not l.strip().startswith('//')]
                    sloc += len(code_lines)
                    content = "".join(lines)

                    for match in class_pattern.finditer(content):
                        public_types += 1
                        bases = match.group(2)
                        if bases:
                            base_items = [b.strip() for b in bases.split(',') if b.strip()]
                            dit = 1 + len([b for b in base_items if not b.startswith('I')])
                            cbo = len(base_items)
                        else:
                            dit = 1
                            cbo = 0
                        dit_list.append(dit)
                        cbo_list.append(cbo)

                    for match in method_pattern.finditer(content):
                        public_methods += 1

    max_dit = max(dit_list) if dit_list else 1
    mean_dit = sum(dit_list) / len(dit_list) if dit_list else 1.0
    mean_cbo = sum(cbo_list) / len(cbo_list) if cbo_list else 0.0
    return public_types, public_methods, max_dit, round(mean_dit, 2), round(mean_cbo, 2), sloc

def count_python_metrics(directory: str) -> Tuple[int, int, int, float, float, int]:
    public_types = 0
    public_methods = 0
    dit_list = []
    cbo_list = []
    sloc = 0

    for root, dirs, files in os.walk(directory):
        if any(ignored in root for ignored in ['tests', 'test', 'examples', 'docs', '.git', '__pycache__', 'dist', 'build']):
            continue
        for f in files:
            if f.endswith('.py'):
                filepath = os.path.join(root, f)
                with open(filepath, 'r', encoding='utf-8', errors='ignore') as fp:
                    raw = fp.read()
                    code_lines = [l for l in raw.splitlines() if l.strip() and not l.strip().startswith('#')]
                    sloc += len(code_lines)
                try:
                    tree = ast.parse(raw, filename=filepath)
                except Exception:
                    continue

                for node in ast.walk(tree):
                    if isinstance(node, ast.ClassDef):
                        if not node.name.startswith('_'):
                            public_types += 1
                            dit = 1 + len(node.bases)
                            dit_list.append(dit)
                            cbo_list.append(len(node.bases))
                            for item in node.body:
                                if isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef)):
                                    if not item.name.startswith('_') or item.name in ('__call__', '__iter__', '__aiter__'):
                                        public_methods += 1
                    elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        if not node.name.startswith('_') and isinstance(getattr(node, 'parent', None), ast.Module):
                            public_methods += 1

    max_dit = max(dit_list) if dit_list else 1
    mean_dit = sum(dit_list) / len(dit_list) if dit_list else 1.0
    mean_cbo = sum(cbo_list) / len(cbo_list) if cbo_list else 0.0
    return public_types, public_methods, max_dit, round(mean_dit, 2), round(mean_cbo, 2), sloc

def count_ts_metrics(directory: str) -> Tuple[int, int, int, float, float, int]:
    public_types = 0
    public_methods = 0
    dit_list = []
    cbo_list = []
    sloc = 0

    type_pattern = re.compile(r'\bexport\s+(?:default\s+)?(?:class|interface|type|enum)\s+(\w+)(?:\s+extends\s+([\w\s,]+))?')
    method_pattern = re.compile(r'\b(?:public\s+)?(?:async\s+)?(\w+)\s*\([^)]*\)\s*(?::\s*[^;{]+)?\s*[{;]')

    for root, dirs, files in os.walk(directory):
        if any(ignored in root for ignored in ['tests', 'test', 'node_modules', '.git', 'dist', 'website']):
            continue
        for f in files:
            if f.endswith('.ts') and not f.endswith('.d.ts'):
                filepath = os.path.join(root, f)
                with open(filepath, 'r', encoding='utf-8', errors='ignore') as fp:
                    lines = fp.readlines()
                    code_lines = [l for l in lines if l.strip() and not l.strip().startswith('//')]
                    sloc += len(code_lines)
                    content = "".join(lines)

                    for match in type_pattern.finditer(content):
                        public_types += 1
                        extends = match.group(2)
                        if extends:
                            bases = [b.strip() for b in extends.split(',') if b.strip()]
                            dit = 1 + len(bases)
                            cbo = len(bases)
                        else:
                            dit = 1
                            cbo = 0
                        dit_list.append(dit)
                        cbo_list.append(cbo)

                    for match in method_pattern.finditer(content):
                        name = match.group(1)
                        if name not in ('if', 'for', 'while', 'switch', 'catch', 'function', 'constructor'):
                            public_methods += 1

    max_dit = max(dit_list) if dit_list else 1
    mean_dit = sum(dit_list) / len(dit_list) if dit_list else 1.0
    mean_cbo = sum(cbo_list) / len(cbo_list) if cbo_list else 0.0
    return public_types, public_methods, max_dit, round(mean_dit, 2), round(mean_cbo, 2), sloc

AGENTCORE_PACKAGES = [
    os.path.join(r"d:\CodeBase\AgentCore-Main", pkg)
    for pkg in ["AgentCore", "AgentCore.Layers", "AgentCore.LLM.Tornado", "AgentCore.Host", "AgentCore.MCP"]
]

def run_structural_extraction() -> List[StructuralMetrics]:
    configs = [
        ("AgentCore", "C#", AGENTCORE_PACKAGES, "csharp", "AgentCore (5 Core Packages: AgentCore, AgentCore.Layers, AgentCore.LLM.Tornado, AgentCore.Host, AgentCore.MCP)"),
        ("LangGraph", "Python", r"D:\CodeBase\Popular Agent Frameworks\langgraph\libs\langgraph\langgraph", "python", r"D:\CodeBase\Popular Agent Frameworks\langgraph\libs\langgraph\langgraph"),
        ("PydanticAI", "Python", r"D:\CodeBase\Popular Agent Frameworks\pydantic-ai\pydantic_ai_slim\pydantic_ai", "python", r"D:\CodeBase\Popular Agent Frameworks\pydantic-ai\pydantic_ai_slim\pydantic_ai"),
        ("OpenAI Agents SDK", "Python", r"D:\CodeBase\Popular Agent Frameworks\openai-agents-python\src\agents", "python", r"D:\CodeBase\Popular Agent Frameworks\openai-agents-python\src\agents"),
        ("Microsoft Agent Framework", "Python", r"D:\CodeBase\Popular Agent Frameworks\microsoft-agent-framework\python\packages\core", "python", r"D:\CodeBase\Popular Agent Frameworks\microsoft-agent-framework\python\packages\core"),
        ("DeepSeek Harness", "TypeScript", r"D:\CodeBase\Popular AI Agnets\deepseek-harness\packages", "ts", r"D:\CodeBase\Popular AI Agnets\deepseek-harness\packages"),
    ]

    results = []
    for name, lang, path, kind, scope_desc in configs:
        if kind == "csharp":
            types, methods, max_dit, mean_dit, mean_cbo, sloc = count_csharp_metrics(path)
        elif kind == "python":
            types, methods, max_dit, mean_dit, mean_cbo, sloc = count_python_metrics(path)
        else:
            types, methods, max_dit, mean_dit, mean_cbo, sloc = count_ts_metrics(path)

        cli = round(0.4 * types + 0.04 * methods + 0.1 * max_dit + 0.1 * mean_cbo, 2)
        results.append(StructuralMetrics(
            framework=name,
            language=lang,
            scope_path=scope_desc,
            public_types=types,
            public_methods=methods,
            max_dit=max_dit,
            mean_dit=mean_dit,
            mean_cbo=mean_cbo,
            sloc=sloc,
            cli=cli
        ))
    return results

if __name__ == "__main__":
    import json
    res = run_structural_extraction()
    for r in res:
        print(f"{r.framework:26} | Types: {r.public_types:4} | Methods: {r.public_methods:4} | MaxDIT: {r.max_dit:2} | MeanCBO: {r.mean_cbo:4} | SLOC: {r.sloc:6} | CLI: {r.cli:6}")

    out_file = os.path.join(r"d:\CodeBase\AgentCore-Main\research\runs", "structural_metrics.json")
    os.makedirs(os.path.dirname(out_file), exist_ok=True)
    with open(out_file, "w", encoding="utf-8") as fp:
        json.dump([asdict(r) for r in res], fp, indent=2)
    print(f"\nSaved structural metrics to {out_file}")
