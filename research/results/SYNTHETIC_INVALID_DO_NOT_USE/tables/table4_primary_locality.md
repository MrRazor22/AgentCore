| Framework Target | Mean P_ext | 95% Bootstrap CI | Normalized I_ext | Radius R_p | Core Touched C_core | API Changes | Expressible Cells | Contrast vs AgentCore (ΔP_ext) | Adj. p-value |
|---|---|---|---|---|---|---|---|---|---|
| **AgentCore** | 0.00 | [0.00, 0.00] | 0.00 | 0.00 | 0.0% | 0.0% | 60/60 | Ref (0.00) | N/A |
| **LangGraph** | 1.63 | [1.37, 1.88] | 0.51 | 1.87 | 0.0% | 16.7% | 60/60 | +1.63 | 0.0000* |
| **PydanticAI** | 1.55 | [1.42, 1.70] | 0.55 | 1.00 | 0.0% | 8.3% | 60/60 | +1.55 | 0.0000* |
| **OpenAI Agents SDK** | 1.40 | [1.20, 1.58] | 0.50 | 1.93 | 0.0% | 15.0% | 60/60 | +1.40 | 0.0000* |
| **Microsoft Agent Framework** | 1.12 | [0.75, 1.43] | 0.42 | 0.82 | 0.0% | 8.3% | 60/60 | +1.12 | 0.0000* |
| **DeepSeek Harness** | 2.23 | [2.05, 2.42] | 0.51 | 2.00 | 0.0% | 33.3% | 55/60 | +2.23 | 0.0000* |

*Note: Holm-Bonferroni adjusted p-values. Asterisk (*) denotes statistically significant contrast at alpha = 0.05.*