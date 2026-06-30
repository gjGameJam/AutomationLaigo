# LAIGO Test-Writing Guidelines

How to write tests in `LaigO.Tests` so they give **trustworthy signals** and stay
maintainable. Read this before adding or changing a test. Companion docs:
`COVERAGE_GAPS.md` (what's tested vs. the backend contract).

---

## 1. Where tests live

| Folder | What goes here | Rule of thumb |
|---|---|---|
| `Contract/` | Fast, read-only, **creates no job** | validation 422s, 404s, gate/CORS/HTTP semantics, live element lookups, pure unit tests (e.g. model round-trips) |
| `Pipeline/` | Slow, stateful, **runs a real generate cycle** | end-to-end lifecycle, quote, optimizer preview, queue concurrency, static mount |
| `Quarantine/` | Known-failing, tracked | `[Explicit]` + `[Category("KnownFailing")]`; excluded from the gating nightly run |

Support code: `ApiClient/` (typed endpoint methods), `Models/` (records matching
the Pydantic shapes), `Diagnostics/` (failure-explainer helpers), `TestConstants.cs`
(well-known ids/bounds), `Fixtures/LaigOTestBase.cs` (per-test context + helpers).

Every test class carries two `[Category]` tags: the **tier** (`Contract`/`Pipeline`)
and the **area** so CI and local runs can filter. Areas in use: `Health`, `Generate`,
`Queue`, `Cors`, `HttpSemantics` (live); `Checkout`, `CheckoutGate`, `Debug`,
`KnownFailing` (shelved/quarantined — see §5). The live pay-what-you-want commerce
path (`/pay`, `/donate`, `/webhooks/stripe`) is **untested** and should land under a
new `Pay` area — see `COVERAGE_GAPS.md` §2.

---

## 2. Naming convention

```
Subject_StateUnderTest_ExpectedBehavior
```

Examples: `Quote_CompletedJob_ReturnsPricedQuoteWithInvariants`,
`Generate_BlockWidthOutOfRange_Returns422`, `Health_DisallowedMethod_Returns405WithAllowHeader`.

**The `ExpectedBehavior` must be something the test actually asserts.** Don't name a
test `…Returns200` if it never checks the status code. If you can't describe the
behavior in one clause, the test is probably doing too much — split it (§3).

---

## 3. Core principles

### Atomic — one behavior per test, no shared mutable state
- Each test sets up its own data. Pipeline tests call
  `SubmitAndAwaitCompletionAsync` to generate **their own** job.
- **We deliberately do NOT use a shared completed-job fixture.** For live-API
  integration tests the cost of a `blockWidth=2` job is cheap on the paid
  always-on instance, and atomicity buys: no cascading failures (one bad generate
  can't fail N tests), and every test is runnable in isolation. Don't add a shared
  fixture to "save time" — it trades away the property that makes failures
  diagnosable.
- If a test name needs the word "and" or "then," it's two behaviors — split it.
  (We split JobStatus-shape vs cache-control, quote-pricing vs quote-idempotency,
  order-list vs optimizer.)

### Honest signals — never pass vacuously
A test that asserts nothing because the "interesting" branch wasn't hit is worse
than no test: it advertises coverage that isn't there.
- If a precondition the test needs isn't met on this instance, call
  `Assert.Ignore("…why…")` so the result reports **skipped**, not passed.
  Examples in this suite: gate disabled → confirm-409 branch unreachable;
  BrickOwl unconfigured → 502 means "API pending," skip the structured-listing
  asserts; `commit` unset locally.
- Don't wrap the only assertion in an `if` and let the `else` fall through silently.

### Assert what the name claims — and the invariants behind it
Weak tests (status code only) catch little. Add the structural contracts:
- **Arithmetic:** `total_cost == Σ seller subtotals`, `customer_total == grand_total + fee`,
  `total_pieces == Σ quantities`, `count fields == list lengths`.
- **Ordering:** `created_at ≤ started_at ≤ finished_at`, `queue_position ≤ queue_length`.
- **Partitions/derivations:** available/unavailable is a clean partition; `cheapest_price`
  is the min of listings.
- **Exact values where fixed:** `MaxWorkers == 1`, `progress == 100` on complete.
- **Structured errors:** assert `detail.code` (frontends match on it) *and* that the
  customer-facing `error` doesn't leak the operator-facing code.

### One round-trip per observation
Don't fetch raw to check the status, then fetch again typed — the second call sees a
different server snapshot and doubles network cost. Fetch once, assert
status/headers/body off the same response (see `HealthTests`).

### Put HTTP plumbing in the client, not the test
Add a method to `LaigOApiClient` rather than building `HttpClient`/multipart inline.
Use the shared static `HttpClient` (never `new HttpClient()` per call — socket
exhaustion). Prefer typed `Models/` records over hand-parsing `JsonDocument`; raw
parsing is only for genuinely dynamic envelopes (e.g. the BrickOwl raw debug shape).

---

## 4. FluentAssertions pitfalls

### ⚠️ `params` overloads have NO `because` argument
This is the one that has bitten us. Most assertions take a trailing `because`
reason string that's ignored unless the assertion fails. But the **collection
`params` overloads do not** — a trailing string is parsed as another *expected
element*, silently changing what you assert. It compiles with no warning.

```csharp
// WRONG — "…round-trip in order" becomes a third expected element.
list.Should().Equal("a", "b", "must round-trip in order");
//   → asserts the list equals ["a","b","must round-trip in order"]

// WRONG — demands the body literally contain the reason text. A healthy
//         server now FAILS this (a false negative that looks like a real bug).
detail.Should().ContainAll("2d", "3d", "the error must list valid values");
```

Methods with this trap (no `because`): `Equal(params)`, `ContainAll(params)`,
`ContainAny(params)`, `ContainInOrder(params)`, `StartWith`/`EndWith(collection)`.

**Do this instead** — reason in a comment, or use a `because`-bearing method:

```csharp
// The order must round-trip exactly.
list.Should().ContainInOrder("a", "b").And.HaveCount(2);

list.Should().BeEquivalentTo(new[] { "a", "b" });          // has options/because

count.Should().Be(2, "two orders were placed");            // scalar → has because

// reason in a comment when the method can't carry one
detail.Should().ContainAll("2d", "3d");  // must list the valid enum values
```

**Rule:** if IntelliSense shows the parameter is `params`, there is no `because`
slot — put the reason in a comment or switch methods.

### Other gotchas
- `BeOneOf(string[], "because")` is fine — the `IEnumerable`+`because` overload binds.
  But `BeOneOf("a", "b")` (params) again has no `because`.
- Header lookups: `response.Headers` keys are **lowercase**
  (`TryGetValue("content-type", …)`).
- A skipped/ignored test is not a failure — prefer `Assert.Ignore` over a no-op pass,
  but never over a real assertion you could make.

---

## 5. Handling live external dependencies

The suite runs against the deployed instance and real LEGO.com / BrickOwl.
- **Unconfigured dependency (BrickOwl → 502):** `Assert.Ignore`, don't pass on 502.
  502 ≠ a verified contract.
- **Known outage you can't fix here (LEGO sourcing):** put the canary in
  `Quarantine/` with `[Explicit]` + `[Category("KnownFailing")]`. The gating nightly
  runs `--filter "Category!=KnownFailing"` so green means green; a non-gating step
  surfaces the quarantine so we notice when it recovers. A permanently-red test in
  the main suite trains everyone to ignore red.
- **Anything that spends real money** (real `/confirm`, payment capture) stays out of
  CI — see `COVERAGE_GAPS.md` §6.
- **Shelved feature (checkout & quote, 2026-06):** the whole checkout/quote suite is
  `[Ignore]`d at the **fixture** level because the flow is disabled in the backend.
  Two groups, both ignored:
  - Customer flow (`[Category("Checkout")]`): `CheckoutConfirm`, `CheckoutGate`,
    `CheckoutStatus`, `QuoteValidation`, Pipeline `Quote`.
  - Sourcing/optimizer behind the quote (`[Category("Debug")]`, all `/checkout-debug/*`
    + `/optimize`): `DebugLookup`, `DebugValidation`, `OptimizerPreview`.

  `[Ignore]` reports **skipped** (honest, not a fake pass) and never runs/fails in CI
  or locally. The fixtures stay in-tree so re-enabling is just deleting the `[Ignore]`
  line. Don't delete them. (The `Quarantine/` LEGO sourcing canary is also `[Ignore]`d
  for the same reason — it relies on LEGO sourcing; its `[Explicit]`/`KnownFailing`
  tags are kept for history.)
- **Storage is JSON, not Neon/Postgres:** the backend persists to JSON files now.
  The one Postgres-only contract — the `ACTIVE_CHECKOUT_EXISTS` 422 on a second
  concurrent checkout (`COVERAGE_GAPS.md` §1.2) — does not fire on the JSON backend,
  so don't write a test asserting it against the current deployment.

---

## 6. Pre-commit checklist for a new/changed test

- [ ] Name is `Subject_State_Expected`, and the `Expected` is actually asserted.
- [ ] One behavior; own setup; no reliance on another test having run.
- [ ] No vacuous pass — unreachable preconditions use `Assert.Ignore` with a reason.
- [ ] Asserts the real contract, not just the status code (invariants from §3).
- [ ] No `because` string passed to a `params` overload (§4).
- [ ] HTTP plumbing lives in `LaigOApiClient`; response parsed via a typed model.
- [ ] Correct folder + both `[Category]` tags (tier + area).
- [ ] `dotnet build` clean (0 warnings); offline tests still green.
