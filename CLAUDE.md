# AutomationLaigo — repo guide

This repo is the **test suite** (.NET 8 / NUnit 3 / Playwright / FluentAssertions 6)
for the LAIGO FastAPI backend. It runs against the deployed instance
(`https://laigo.onrender.com`, override via `LAIGO_BASE_URL`) on a nightly GitHub
Actions schedule. The backend source lives in a **separate** repo at
`C:\Users\Grant Benson\OneDrive\Desktop\LAIGO` (`scripts/Main.py`,
`scripts/pay_router.py`, `scripts/checkout/*`) — read it to verify endpoint
contracts before asserting them.

## What LAIGO does

A FastAPI service that turns an uploaded photo into a LEGO-mosaic build pack
(instructions PDF + part list, zipped). Submit a job, poll it, download the
artifact. The build pack is sold as a **pay-what-you-want digital product**
(name your price, $0 allowed) via Stripe.

## Live API surface (what's mounted — verify in `Main.py`/`pay_router.py` before asserting)

| Area | Endpoints | Status |
|---|---|---|
| Mosaic pipeline | `GET /health`, `GET /`, `GET /queue`, `POST /generate` (⚠️ per-IP rate limit: 1 per 20s → 429 + `Retry-After`), `GET /jobs/{id}`, `GET /jobs/{id}/preview`, `GET /jobs/{id}/download`, `GET /artifacts/{id}/artifact.zip` (static mount) | **Live, well covered** |
| Commerce (pay-what-you-want) | `POST /jobs/{id}/pay`, `POST /donate`, `POST /webhooks/stripe` (all in `pay_router.py`) | **Live, ⚠️ ZERO test coverage** — see `COVERAGE_GAPS.md` §2 |
| Gate | `GET /checkout/gate` | Live; tests `[Ignore]`d by policy |

### ⚠️ Shelved (NOT mounted — these endpoints 404)
The **checkout saga** (`checkout/router.py`: `/checkout/quote|confirm|status` +
marketplace ordering) and **checkout-debug** (`debug_router.py`: `/checkout-debug/*`
LEGO/BrickOwl sourcing + `/optimize`) are shelved (`Main.py:63-66`). Their test
fixtures are `[Ignore]`d, not deleted. The backend also runs **JSON storage, not
Neon/Postgres**, and refuses to boot with `CHECKOUT_ENABLED=true` on JSON
(`Main.py:274-289`) — so checkout is necessarily off. Full detail:
`COVERAGE_GAPS.md` §What-changed.

## Test repo structure (`LaigO.Tests/`)

- `Contract/` — fast, read-only, **creates no job** (validation 422s, 404s, CORS, HTTP semantics, health, queue shape).
- `Pipeline/` — slow, stateful, **runs a real generate cycle** (lifecycle, artifact ZIP, queue concurrency, static mount).
- `Quarantine/` — `[Explicit]`+`[Category("KnownFailing")]`, excluded from the gating run.
- `ApiClient/LaigOApiClient.cs` — typed endpoint methods (the only place HTTP plumbing lives).
- `Models/` — records mirroring the Pydantic response shapes.
- `Fixtures/` — `GlobalSetup` (shared Playwright) + `LaigOTestBase` (per-test context + `SubmitAndAwaitCompletionAsync`).
- `Diagnostics/ArtifactDiagnostics.cs` — failure-explainer for artifact ZIPs.
- `TestConstants.cs` — well-known ids/bounds; `TestConfig.cs` — base URL + timeouts.

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
- **Rate-limit pacing:** every `/generate` call that passes param validation goes
  through the client's `GenerateRateLimitGate` (`GenerateAsync` is always gated;
  raw methods take `gated: true`). Never `Task.Delay` in a test to space submissions.
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
