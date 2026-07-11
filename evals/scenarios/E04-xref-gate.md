# E04 — Xref gate

## Setup
- Drawing with a missing or unloaded xref (lab fixture).

## Call
`forge_qa_check_xrefs` and/or `forge_qa_preflight`

## Expect
- Preflight or xref check reports unhealthy xref
- `passed=false` when severity error
