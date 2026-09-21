| Framework Target | Unrelated Concern Leakage (L_leak) | Mean Tests Affected (F_aff) | Mean Fix Files (F_fix) | Core Edited in Fix (C_fix) | Mean Repair Time (s) | Verdict |
|---|---|---|---|---|---|---|
| **AgentCore** | 0.0% | 0.0 | 1.0 | 0.0% | 43.1s | Strictly Isolated |
| **LangGraph** | 80.0% | 2.6 | 2.2 | 0.0% | 127.9s | Systemic Leakage |
| **PydanticAI** | 80.0% | 2.4 | 1.8 | 0.0% | 88.1s | Systemic Leakage |
| **OpenAI Agents SDK** | 60.0% | 1.6 | 1.6 | 0.0% | 77.3s | Partially Isolated |
| **Microsoft Agent Framework** | 40.0% | 1.4 | 1.4 | 0.0% | 69.1s | Partially Isolated |
| **DeepSeek Harness** | 60.0% | 2.2 | 1.8 | 0.0% | 127.1s | Systemic Leakage |