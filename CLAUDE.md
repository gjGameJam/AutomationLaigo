# AutomationLaigo — repo guide

This repo is the **test suite** (.NET 8 / NUnit 3 / Playwright / FluentAssertions 6)
for the LAIGO FastAPI backend. It runs against the deployed instance
(`https://laigo.onrender.com`, override via `LAIGO_BASE_URL`) on a nightly GitHub
Actions schedule. The backend source lives in a **separate** repo at
`C:\Users\Grant Benson\OneDrive\Desktop\LAIGO` (`scripts/Main.py`,
`scripts/checkout/*`) — read it to verify endpoint contracts before asserting them.

## Test conventions — read `LaigO.Tests/TESTING_GUIDELINES.md` before writing tests

Top rules (full detail in that doc):
- **Folders by tier:** `Contract/` (fast, no job), `Pipeline/` (real generate cycle),
  `Quarantine/` (`[Explicit]`+`KnownFailing`, excluded from the gating run).
- **Naming:** `Subject_StateUnderTest_ExpectedBehavior`, and the expected behavior
  must actually be asserted.
- **Atomic:** one behavior per test; each test makes its own data;
  **no shared completed-job fixture** (deliberate — keeps failures diagnosable).
- **Honest signals:** unreachable preconditions use `Assert.Ignore`, never a vacuous
  pass; unconfigured deps (BrickOwl 502) → Ignore, not pass.
- **Strong assertions:** assert the invariants (arithmetic, ordering, partitions,
  structured-error `code`), not just the status code.
- **⚠️ FluentAssertions `params` overloads have NO `because` argument**
  (`Equal`, `ContainAll`, `ContainAny`, `ContainInOrder`): a trailing reason string
  becomes an *expected element* and silently corrupts the assertion. Put the reason
  in a comment or use a `because`-bearing method (`HaveCount`, `Be`, `BeEquivalentTo`).

## Workflow
- Verify changes compile: `dotnet build AutomationLaigo.sln -c Release`.
- Offline tests (model round-trips) run without network; full suite hits live prod
  and spends generate cycles — don't run it casually.
- CI: `.github/workflows/nightly.yml` gates on `Category!=KnownFailing`.
- Coverage status / open gaps: `LaigO.Tests/COVERAGE_GAPS.md`.
