# Findings - <yyyy-MM-dd> review

Severity rubric: Critical (process dies / data lost on a normal action), High (freeze > 5 s,
unbounded leak, crash on a plausible path), Medium (noticeable waste, nothing breaks), Low (hygiene,
hazard with no demonstrated trigger), Info. Status: Fixed, Mitigated, Accepted (reason), Deferred
(reason), Suspected (not yet proven), False positive.

| ID | Severity | Status | Title | Proof |
|---|---|---|---|---|
| PR-001 | | | | |

---

### PR-001 - <short title>  `[Severity]` `[Status]`

- **Evidence:** the number, dump identifier, log line or `file:line` that shows it. Include the
  command that produced it.
- **Root cause:** the mechanism, not the symptom. Name the code path.
- **Impact:** who hits it, how often, what they see.
- **Fix:** what changed and where (`file:line`), in one or two sentences. Why this and not the obvious alternative.
- **Proof:** the regression test (`Namespace.Class.Method`) and/or the measurement, before -> after.
- **Residual risk / follow-up:** what is still not covered, if anything.
