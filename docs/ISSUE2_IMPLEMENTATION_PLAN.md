# Issue #2 Implementation Plan - RENA_TIPONACIONALIDAD extraction

Scope:
- ORIGEN => extract Departure only
- DESTINO => extract Return only
- AMBOS => extract both tabs

Tasks:
1. Add selector strategy helpers for tab targeting in `SherpaScraperService`.
2. Ensure orchestrator passes nationality type context to scraping call.
3. Persist partial extraction safely (null fields allowed for non-target tab).
4. Add integration test matrix for ORIGEN/DESTINO/AMBOS.
5. Verify no regressions on AI HTML and fallback behavior.

Acceptance:
- Build green
- Tests green
- Smoke on COL/COL/CAN + synthetic ORIGEN/DESTINO cases
