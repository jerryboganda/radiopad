# Definition of Ready

**Status:** Current  ·  **Owner:** Product + Engineering  ·  **Last Updated:** 2026-05-04

A backlog item is **ready** to enter a sprint / Ralph loop only when:

- [ ] Title concisely captures the user value.
- [ ] Description includes rationale tied to a persona / KPI / risk.
- [ ] Acceptance criteria are testable (Given / When / Then or equivalent).
- [ ] Surfaces affected (web / desktop / mobile / CLI / backend) are listed.
- [ ] If UI: linked design intent + which locked tokens / components apply.
- [ ] If AI: prompt id and provider class confirmed.
- [ ] If clinical: rulebook id + golden case impact noted.
- [ ] Security & privacy review:
  - PHI flow change? Yes / No (if Yes, attach a sketch + reviewer).
  - Audit log change? Yes / No.
  - Tenant isolation change? Yes / No.
- [ ] Dependencies identified (other tickets / external vendors / migrations).
- [ ] Estimated relative effort.
- [ ] Owning engineer & reviewer assigned.

If the answer to any of the above is "we'll figure that out during implementation", the item is **not ready**. Send it back to grooming or open the gap as a planning task first.
