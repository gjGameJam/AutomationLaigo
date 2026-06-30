# LAIGO API Test Coverage Gap Audit

**Author:** Senior SDET review
**Date:** 2026-05-31 (supersedes the 2026-05-24 audit)
**Scope:** LAIGO FastAPI backend (`scripts/Main.py`, `scripts/checkout/router.py`, `scripts/checkout/debug_router.py`, `scripts/checkout/gate_router.py`, `scripts/checkout/models.py`, `scripts/checkout/optimizer.py`) vs. `LaigO.Tests`.
**Method:** Endpoint-by-endpoint cross-reference of the live Python contract against the C# client + test classes; every documented status branch and response invariant enumerated and matched to a concrete `[Test]`.

Severity legend:
- **P0** — Test gives a wrong signal *right now* (model drift, silent always-pass, dead assertion).
- **P1** — Endpoint or significant status branch entirely unverified; customer-visible regression risk is high.
- **P2** — Validation / edge case unverified; lower blast radius but still a real contract.
- **P3** — Defensive/observability paths; nice-to-have.

---

## Changelog 2026-06-03 — antipattern audit + restructure + coverage build-out

Re-verified every claim below against current `scripts/` and rebuilt the suite. Suite grew from ~24 tests to **73**. Folder layout reorganized into `Contract/` (fast, no job created), `Pipeline/` (slow, stateful), `Quarantine/` (KnownFailing). CI now runs `--filter "Category!=KnownFailing"` as the gating job plus a non-gating quarantine step.

**P0s — both FIXED:**
- 0.1 `CheckoutStatusResponse` model drift → `CheckoutModels.cs` renamed `stripe_payment_intent_id`→`payment_hold_id`, added `payment_authorized_cents`, `customer_message`, `manual_review_reason`. Verified by `Status_DeserializesEverySagaStatusValue` (8 cases, all green).
- 0.2 dead `Traceback` assertion → field removed from `JobModels.cs`; assertion deleted.

**Now CLOSED (new tests, verified compiling):**
1.1 preview (`Preview_NonExistentJob_Returns404WithCode`, `Preview_CompletedJob_ReturnsJsonPayload`) ·
1.2 confirm CI-safe branches (`Confirm_GateClosed_Returns503WithCode`, `Confirm_QuoteNotFound_Returns409`) ·
1.3 static mount (`Artifacts_StaticMount_*`) ·
2.1 generate value-range 422s + `mosaic_type=3d` happy path ·
2.4 quote validators + free-shipping invariant + `expires_at` bounded + repeated-quote distinct id ·
2.5 saga enum round-trip ·
2.6 debug 422 validators + `id_type` variants ·
2.7/2.8 order-list & optimize 404s + free-shipping ·
3.2 CORS allow-list ·
3.4 two-job concurrency (`Queue_TwoConcurrentJobs_SecondIsQueuedBehindFirst`) ·
3.5 `/jobs/{id}` cache-control ·
4.x HEAD + 405.

**Antipatterns addressed:** vacuous conditional passes → `Assert.Ignore` (gate/BrickOwl-pending now report *skipped*, not *passed*); always-red canary → `[Explicit]`+`KnownFailing` quarantine, excluded from gate; per-call `HttpClient` → shared static; raw `JsonDocument` parsing → typed debug models; inline ZIP diagnostic → `Diagnostics/ArtifactDiagnostics`; magic values → `TestConstants`.

### Atomicity + assertion-strengthening pass (later same day)
Decision: **no shared completed-job fixture — tests stay atomic.** For live-API integration tests the best-practice default is per-test independence unless setup is prohibitively expensive; `blockWidth=2` jobs on the 32×32 image are fast on the paid always-on instance, so atomicity wins (no cascading failures, every test runnable in isolation). `SubmitAndAwaitCompletionAsync` gives each test its own job.
- Split bundled behaviors for atomicity: JobStatus shape vs cache-control; quote pricing vs quote idempotency; order-list vs optimizer preview.
- Strengthened assertions across every test: exact capacity constants (`MaxWorkers==1`, `MaxQueueSize==20`); queue uniqueness; timestamp ordering `created≤started≤finished`; `progress==100` exact; download `Content-Type`/`Content-Disposition`; preview JSON-object shape; CORS credentials header; 405 `Allow` header; HEAD empty body; arithmetic invariants (`total_cost == Σ seller subtotals`, `customer_total == grand_total + fee`); LEGO/BrickOwl batch partition consistency; `cheapest_price`/`most_stock` derivation; structured-error `error`/`code`/`mode` fields; saga round-trip of all optional fields + omitted-fields-as-null. 77 tests total.
- Gotcha fixed: FluentAssertions `Equal(...)`/`ContainAll(...)` params overloads have no `because` arg — a trailing reason string becomes an expected element. Reasons moved to comments / `HaveCount`.

**Still open / intentionally deferred:**
- **A4 shared fixture** — declined in favor of atomicity (see decision above). Each pipeline test runs its own generate cycle.
- §3.1 global-handler shape (no reliable trigger), §3.3 XFF (no audit-fetch endpoint), §6 out-of-scope (real confirm, 413/429, timeouts, disk failures), deterministic `failed` job. See §6.
- memory issue #11 — `GenerationTimeoutMs=600000` still too long for `blockWidth=2`.

---

## Changelog since 2026-05-24

Re-audited against current `scripts/` and current `LaigO.Tests/`. The following gaps from the prior audit are **now closed** — do not re-report them:

| Prior item | Status | Evidence |
|---|---|---|
| 0.1 `PostLegoElementsAsync` wrong body shape | **FIXED** | `LaigOApiClient.cs:219-225` now sends `{ element_ids = [...] }` |
| 0.3 `QueueResponse` dead fields (`known_jobs`, `counts.complete/failed`) | **FIXED** | `JobModels.cs:34-49` matches backend exactly; `QueueTests` asserts `QueuedJobIds.Count == Counts.Queued` |
| 0.4 `queue_size` vs `queue_length` | **FIXED** | `JobModels.cs:22-23` now `queue_position` + `queue_length` |
| 0.5 optimizer preview empty-body silent-pass | **FIXED** | `LaigOApiClient.cs:239` sends a body; `DebugTests.cs:287` now `BeOneOf(200,502)`, treats 404/422/500 as hard fail |
| 0.6 `ParseAsync` no status check | **FIXED** | `LaigOApiClient.cs:29-31` throws on `!response.Ok` |
| 1.3 LEGO listing `available`/`listing` invariant unverified | **FIXED** | `DebugTests.cs:172-178` asserts `listing` null iff `available==false` |
| LEGO sourcing health + price-parser drift undetected | **COVERED** | `LegoListing_CanaryElement_IsSourceableWithRealPrice` (canary `302421`); hard-fails on `available==false` (sourcing down) and on `available + price==0` (price parser drift) |
| Generate 400 invalid-image branch (was missing from prior audit) | **COVERED** | `Generate_InvalidFile_Returns400WithDetail` (`GenerateTests.cs:182`) |
| Generate missing-field 422 | **COVERED** | `Generate_MissingRequiredField_Returns422WithValidationError` |
| Generate `to_frame` echo + `progress≈100` | **COVERED** | `Generate_WithToFrame_CompletesSuccessfullyAndEchoesSetting` |
| `/jobs/{id}` timestamp fields (`created_at`/`queued_at`/`finished_at`) | **COVERED** | `Generate_ValidImage_JobQueuesAndCompletes` asserts all three |
| Artifact ZIP content (manifest/order_list/instructions PDF) | **COVERED** | `Generate_CompletedJob_ArtifactIsValidMosaicZip` |
| Order-list positive shape + totals | **COVERED** | `OptimizerPreview_WithCompletedJob_ReturnsAllocation` |
| Entire `/checkout/gate` endpoint | **COVERED** | new `CheckoutGateTests` (8 tests) |
| Memory issue #9 — Health/Queue double-fetch | **FIXED** | both now use a single typed call |

**Net:** the suite went from 5 open P0s to **1**, and added ~13 tests. The remaining gaps below are what's left.

---

## ⚠️ Live backend defect found during this audit (2026-05-31): LEGO sourcing is DOWN

`LegoListing_CanaryElement_IsSourceableWithRealPrice` (element `302421`, a known-stocked piece) found the deployed backend returning `{available: false, listing: null}`. Root cause confirmed by replicating `_search()` against the live endpoint:

> `GET https://www.lego.com/api/product/search/en-US?q=302421&...` → **HTTP 404** (clean route-level 404 from LEGO's servers — endpoint removed/moved, not a bot block).

`_LEGO_SEARCH_URL` (`scripts/checkout/clients/lego_client.py:55`) is dead, so `_search()` returns `None` for **every** element → all LEGO availability/price/listing calls fail → live `/quote` routes every piece to unsourceable and `can_proceed` is `false`. This is the LEGO MVP's only sourcing path.

Endpoint-migration probe (2026-05-31): the old REST search and its variants all 404, but `GET https://www.lego.com/api/graphql` returns **HTTP 400** (route exists, expects a POST body) — i.e. **LEGO moved Pick-a-Brick to a GraphQL API**.

**Backend action (separate repo, not the test suite — deferred by owner 2026-05-31, test intentionally left red as the signal):** rewrite `_search` to POST the Pick-a-Brick GraphQL query to `https://www.lego.com/api/graphql` (capture the exact `operationName`/`query`/`variables`/headers from DevTools → Network on the live PaB page), update `_parse_available`/`_parse_price_cents` to the GraphQL response shape, then **redeploy to Render** — the deployed instance still runs the old REST code.

**Observability gap this exposed (test-suite-relevant):** LEGO has **no `/raw` debug endpoint**, unlike BrickOwl (`/checkout-debug/brickowl/element/{id}/raw`). The backend collapses "endpoint 404'd", "availability parser drifted", and "genuine stockout" into one `available: false`, so neither tests nor operators can tell them apart. **Recommend the backend add `GET /checkout-debug/lego/element/{id}/raw`** returning the raw search HTTP status + JSON; then a test could assert "search returned a result" separately from "parsed as available", pinpointing which link broke.

---

## 0. P0 — Active wrong-signal defects in the test suite

### 0.1 `CheckoutStatusResponse` model is stale vs the L5 provider-agnostic rename (STILL OPEN)

**File:** `Models/CheckoutModels.cs:45-54`. Backend: `models.py:170-196`.

The C# record still declares `stripe_payment_intent_id` and is missing four fields the backend emits:

| Backend field (`models.py`) | Test model | Effect |
|---|---|---|
| `payment_hold_id` | `stripe_payment_intent_id` (wrong name) | always deserializes to `null` — any future `NotBeNull` assertion passes on a null and hides a regression |
| `payment_authorized_cents` | (missing) | silently dropped |
| `customer_message` | (missing) | the B12/H1 customer-facing string — the *only* string safe to surface to a user — is never observable from a test |
| `manual_review_reason` | (missing) | the MANUAL_REVIEW saga branch is never observable |
| `saga_status` | `string` | backend returns a `SagaStatus` enum value; `string` deserializes fine, but no test ever asserts a specific enum value round-trips |

This is the single highest-impact silent-failure in the suite: `/status` is the saga's only observable API contract, and the model can't see its modern shape. **Fix:** rename `StripePaymentIntentId → PaymentHoldId` (`payment_hold_id`), add `payment_authorized_cents`, `customer_message`, `manual_review_reason`. Then add the deserialization unit test in 2.5.

### 0.2 `JobStatusResponse.Traceback` maps to a field the backend never emits (dead assertion)

**File:** `Models/JobModels.cs:17`; assertion at `GenerateTests.cs:31`.

`_store_row_to_response` (`Main.py:526-567`) builds the `/jobs/{id}` body and only ever adds `error` (from `error_message`) — there is **no `traceback` key** in any `/jobs/{id}` response. So `JobStatusResponse.Traceback` always deserializes to `null`, and `finished.Traceback.Should().BeNull("a completed job has no traceback")` passes unconditionally for *every* status, including a failed job. The assertion looks like it guards failure-mode hygiene but tests nothing. **Fix:** either drop the field + assertion, or (better) point it at the real failure surface — a failed job carries its detail in `error`, so assert on that in the failed branch instead.

---

## 1. P1 — Endpoints with no test at all

### 1.1 `GET /jobs/{job_id}/preview` — zero coverage (STILL OPEN)

Endpoint at `Main.py:1424-1455`; not present in `LaigOApiClient` and no `PreviewTests` class exists. Three branches all unverified:

| Branch | Trigger | Expected | Risk |
|---|---|---|---|
| 200 | `preview.json` exists | `Response` body, **`Content-Type: application/json`** (NOT `octet-stream` — it uses `Response`, not `FileResponse`) | Frontend 3D preview silently breaks if shape/content-type drifts |
| 404 | file missing | `{detail: {error, code: "PREVIEW_NOT_AVAILABLE"}}` | Frontend matches on `detail.code`; a code rename breaks it invisibly |
| 500 | unreadable | `{detail: {error, code: "PREVIEW_CORRUPTED"}}` | — |

Add a client method + two tests: positive after `WaitForJobAsync` (assert content-type is JSON and body parses), and 404 for a non-existent job asserting `detail.code == "PREVIEW_NOT_AVAILABLE"`.

### 1.2 `POST /jobs/{job_id}/checkout/confirm` — the safety/idempotency branches are reachable without spending money (STILL OPEN)

`router.py:171-307` documents five non-200 codes. None are tested; several are pure-read and CI-safe:

| Code | Trigger | CI-safe? |
|---|---|---|
| 503 | gate closed → `detail.code == "CHECKOUT_GATE_CLOSED"` | **Yes** — no payment staged. Reachable now: the gate is `disabled` on the test instance unless `CHECKOUT_ENABLED` + `sk_test_` are set (cross-check with `/checkout/gate` `mode`). |
| 409 | confirm a `checkout_id` that never existed → "Quote expired or not found" | **Yes** — pure read, no quote needed |
| 404 | confirm a valid `checkout_id` under the wrong `job_id` | needs one `/quote` (costs nothing) |
| 422 | quote has unsourceable items, then confirm | needs a staged quote |
| 422 | second confirm, different `checkout_id`, same job → `detail.code == "ACTIVE_CHECKOUT_EXISTS"` (Postgres backend only; JSON backend won't raise) | needs two quotes |
| 409 | confirm twice, same `checkout_id` → second body includes `saga_status` + `poll_url` (B14) | needs a real confirm (happy path) — **out of scope** |

Only the happy path truly requires real money. **At minimum add `Confirm_GateClosed_Returns503WithCode` and `Confirm_QuoteNotFound_Returns409`** — both are pure-read and need no client method beyond `ConfirmCheckoutRawAsync` (already exists).

### 1.3 Static `/artifacts/{job_id}/artifact.zip` mount — untested (STILL OPEN)

`Main.py:449` mounts the entire `OUTPUT_DIR` as `/artifacts` via `StaticFiles`. This is a **second, unauthenticated download path** parallel to `/jobs/{id}/download`, with different semantics (no `mosaic_{id}.zip` rename, no `Content-Disposition`, directory-style path traversal surface).

Two reasons to test it:
1. If intentional (frontend serves previews/assets from it), it deserves one shape test so a mount removal is caught.
2. If unintentional, **it is an information-disclosure surface** — anyone who learns a `job_id` can fetch the artifact, and possibly enumerate the directory — and a test asserting the *intended* behavior documents the decision.

Add one test: `GET /artifacts/{completed_job}/artifact.zip` returns 200 + ZIP magic bytes, and decide explicitly whether a non-existent path should 404 (it will).

---

## 2. P1 — Major status branches / invariants per endpoint, still untested

### 2.1 `POST /generate` — value-range validation 422s (`Main.py:1224-1255`)

The missing-*field* 422 is covered; the value-*range* guards are not. Order is now contractually fixed (B54): **validation 422 fires before the 503 shutdown check, the 429 queue-full check, the 413 size check, and the 400 image-verify** — so these are cheap (the request is rejected before the file is even read, and a throwaway/invalid image works fine):

| Branch | Trigger | Tested? |
|---|---|---|
| width below `MIN_BLOCK_WIDTH` (=1) → 422 | `mosaic_block_width=0` | No |
| width above `MAX_BLOCK_WIDTH` (env, default 40) → 422 | `mosaic_block_width=41` | No |
| width boundaries accepted | `1` and `40` → 200/queued | No |
| `mosaic_type` not in `{"2d","3d"}` → 422 (detail lists valid values) | `mosaic_type="cube"` | No |
| **`mosaic_type="3d"` happy path** | the only other enum value | No — only `"2d"` ever exercised |
| `background_color_percent < 0` → 422 | `-1` | No |
| `background_color_percent > 100` → 422 | `101` | No |
| `background_color_percent` boundaries accepted | `0` and `100` | only `100` (default) |
| 503 shutdown / 429 queue-full / 413 too-large / 500 disk | — | No (impractical in CI — see §6) |

The first seven are the high-value adds: parameterized, no completed job needed, and they pin the documented ordering contract.

### 2.2 `GET /jobs/{job_id}` — non-complete status branches (`Main.py:1371-1421`)

Only the `complete` branch has invariant coverage. Still untested:

| Branch | Tested? |
|---|---|
| `status=queued` with `queue_position` + `queue_length` populated | No (now *observable* since 0.4 fixed — submit a job behind a running one and poll) |
| `status=running` with `progress` strictly in `[0,100)` mid-flight | No (cheap: submit, poll once before completion) |
| a **real** `failed` job with `error` populated | No — `Generate_JobStatus_ReturnsValidShape` has a conditional `if failed` branch but no test deterministically produces a failed job |
| `timed_out` normalized to `failed` for the client (`_store_row_to_response:541`) | No (impractical — `JOB_TIMEOUT_SECONDS=1800`) |
| manifest-on-disk fallback (`Main.py:1380-1389`) | No |
| `started_at` absent when queued / present when running | No |
| eviction after `JOB_TTL_SECONDS` (default 600) → 404 | No (slow; see §6) |

Note on field names for assertion authors: the response uses **`finished_at`** (not `completed_at`), and `created_at == queued_at`. `progress` is forced to `100` on complete and `0` on failed/timed_out by the handler.

### 2.3 `GET /queue` — dynamic / ordering invariants (`Main.py:1177-1204`)

Static structure + consistency is covered. The *dynamic* contract is not:

- `queued_job_ids` ordered by `queued_at` (FIFO).
- A `/generate` submitted while the worker is busy increments `queued_jobs` and appends to the tail of `queued_job_ids`.
- `active_jobs` never exceeds `max_workers` (=1) across observations.

Reachable on the paid Render instance: submit two `blockWidth=2` jobs, poll `/queue` between them, assert the second appears in `queued_job_ids`. (See 5.x.)

### 2.4 `POST /jobs/{job_id}/checkout/quote` — request validation + determinism (`QuoteRequest`, `models.py:8-13`; `router.py:69-168`)

Positive path + invalid-job 404 are covered. Untested:

| Validator / invariant | Tested? |
|---|---|
| `shipping_country` not exactly 2 chars → 422 | No |
| `shipping_zip` empty or > 20 chars → 422 | No |
| `customer_email` missing → 422 | No |
| empty `order_list.json` → 422 "Order list is empty" | No |
| repeated `/quote` on same job → **different `checkout_id` each call** (`secrets.token_hex(8)`, `router.py:130`) | No |
| `expires_at` bounded near `now + 600` (currently only asserted `> now`) | No — a TTL set to 1s would still pass |
| **LEGO free-shipping invariant**: any seller with `seller_id == lego_official` and `piece_cost_cents >= 3500` (`LEGO_FREE_SHIPPING_THRESHOLD_CENTS`) must have `shipping_cost_cents == 0` (`optimizer.py:207-252`) | No — cheap add to the existing quote test, no new test needed |
| non-US `shipping_country` shipping path | No |

To hit the Pydantic 422s rather than the order-list 404, use a **completed** job_id with a malformed body.

### 2.5 `GET /jobs/{job_id}/checkout/{checkout_id}/status` — saga enum round-trip (`router.py:310-332`)

After fixing model drift (0.1), add a **pure unit test** (no network) that feeds canned JSON for each `SagaStatus` value (`initiated`, `stripe_held`, `orders_placed`, `fallback_ordered`, `payment_captured`, `compensated`, `failed`, `manual_review`) into `JsonSerializer.Deserialize<CheckoutStatusResponse>` and asserts the field + the `customer_message`/`manual_review_reason` pairing round-trips. Cheap insurance against the next provider rename (this is exactly the class of break that L5 caused and 0.1 still reflects). The live integration path for terminal states is out of scope (§6).

### 2.6 Debug endpoints — input-validation 422s (`debug_router.py:42-83`)

The Pydantic models declare hard bounds none of the tests exercise:

| Endpoint | Validator | Tested? |
|---|---|---|
| `POST /checkout-debug/brickowl/elements` | `element_ids` empty → 422 | No |
| `POST /checkout-debug/brickowl/elements` | `len(element_ids) > 100` → 422 | No |
| `POST /checkout-debug/brickowl/elements` | `shipping_country` length ≠ 2 → 422 | No |
| `POST /checkout-debug/brickowl/elements` | `shipping_zip` length > 20 → 422 | No |
| `POST /checkout-debug/lego/elements` | same four validators (`element_ids` only) | No |
| `POST /checkout-debug/job/{id}/optimize` | `shipping_country` length ≠ 2 → 422 | No |
| `GET /checkout-debug/brickowl/element/{id}/raw?id_type=` | `id_type` variants (`design_id`, `bl_item_no`, `set_number`) | only default `item_no` |

### 2.7 `GET /checkout-debug/job/{id}/order-list` — 404 branch (`debug_router.py:279-283`)

Positive shape is covered (in `OptimizerPreview_WithCompletedJob_ReturnsAllocation`). The 404 for a missing order list (non-existent job) is not. One-liner.

### 2.8 `POST /checkout-debug/job/{id}/optimize` — 404 + body-validation 422 + free-shipping invariant (`debug_router.py:293-383`)

Positive 200/502 path is covered. Add:
- 404 for a non-existent job_id (one line).
- The same free-shipping invariant as 2.4, asserted on the preview's seller list.
- Note for assertion authors: `/optimize` and `/quote` use **different field names** for the same numbers — `/optimize` returns `grand_total_cents` (pieces+shipping pre-fee), `laigo_fee_cents`, `customer_total_cents`; `/quote` returns `total_cost_cents`, `laigo_service_fee_cents`, `grand_total_cents`. The existing tests document this; keep it in mind when adding cross-checks.

---

## 3. P2 — Cross-cutting concerns with no coverage

### 3.1 Global exception handler shape (`Main.py:497-510`)

Returns `{detail: "Internal server error", request_id: <hex>}` and (B58) **no longer echoes the exception class/message** — that's a deliberate info-disclosure fix. A regression that re-adds the exception text to the body would be a security regression invisible to the suite. A contract test that reliably triggers the handler is brittle, but worth one attempt; lower priority because the failure mode is operator-log-observable.

### 3.2 CORS allow-list (`Main.py:454-463`)

`allow_origins` is restricted to the frontend + `localhost:5173`, `allow_credentials=true`. A regression loosening this to `["*"]` is invisible from the current tests. Add one OPTIONS preflight to `/health` with `Origin: https://attacker.example.com` and assert no `Access-Control-Allow-Origin` echo for the disallowed origin (and that the allowed origin *is* echoed).

### 3.3 X-Forwarded-For middleware (`Main.py:481-490`)

`request.state.real_ip` (leftmost XFF entry) is the audit source-of-truth for checkout. Not surfaced via any API today, so untestable from the C# suite — **P2 placeholder**: the moment an audit-fetch debug endpoint ships, an `X-Forwarded-For: 1.2.3.4` → audit-row assertion should be the first test on it.

### 3.4 Single-worker concurrency invariants (`MAX_WORKERS=1`, `MAX_QUEUE_SIZE=20`)

The core dispatch contract is unverified: a second `/generate` while one runs enters `queued` not `running`; the first job's completion releases the second within a scheduler tick; `active_jobs` never exceeds `max_workers` while observed. A two-job sequence with mid-flight `/queue` + `/jobs/{id}` polling closes this and 2.2/2.3 together.

### 3.5 `Cache-Control` on polling endpoints

`/checkout/gate` is the only endpoint whose cache header is asserted (`no-store`). The frontend polls `/jobs/{id}` every 2–5s; if a proxy ever cached it, status would freeze. A negative assertion (no `Cache-Control`, or `no-store`) on `/jobs/{id}` would catch that class cheaply.

---

## 4. P3 — Defensive paths & hygiene

- **Method-not-allowed (405):** e.g. `DELETE /jobs/{id}`, `PUT /health`. Untested.
- **Trailing-slash routing:** `/jobs/{id}/` vs `/jobs/{id}` — FastAPI behavior differs per route; untested, can mask deployment-config drift.
- **HEAD requests:** `HEAD /health`, `HEAD /jobs/{id}/download` (frontend "is the artifact ready" precheck). FastAPI auto-derives HEAD from GET; untested, so a future custom route dropping HEAD would go unnoticed.
- **`appsettings.test.json` `GenerationTimeoutMs=600000`** (memory issue #11, still open): for `blockWidth=2` jobs a separate ~120 s timeout would surface a worker hang ~5× faster in nightly CI.
- **`Download_IncompleteJob_Returns404_ThenSucceedsWhenComplete`** (memory issue #10): the pre-completion 404 still relies on generation taking multiple seconds. Acceptable on the paid instance; documented here for the record.

---

## 5. Recommended additions, ordered by ROI

Highest-impact, lowest-cost first. Each is a single `[Test]` unless noted.

1. **Fix 0.1** (`CheckoutStatusResponse` model) — non-negotiable; it produces silent always-pass on the saga's only observable contract.
2. **Fix 0.2** (`Traceback` dead assertion) — drop or repoint it.
3. **`Confirm_GateClosed_Returns503WithCode`** + **`Confirm_QuoteNotFound_Returns409`** — pure-read, no money, client method already exists; cross-check gate `mode` first.
4. **`Generate_InvalidWidth_Returns422` (params: 0, 41, -1)** — no completed job, throwaway image; pins the validation-before-everything ordering.
5. **`Generate_InvalidMosaicType_Returns422`** + **`Generate_3DMosaic_Completes`** — closes the only enum's untested value, both directions.
6. **`Generate_InvalidBackgroundPercent_Returns422` (params: -1, 101)** + **`Generate_BackgroundPercent_Boundaries` (0, 100 → 200)**.
7. **`Preview_NonExistentJob_Returns404WithCode`** + **`Preview_CompletedJob_ReturnsJson`** — closes the whole `/preview` gap; assert content-type is `application/json`.
8. **`Quote_InvalidShippingCountry_Returns422`** (completed job + malformed body, to hit Pydantic not the order-list 404).
9. **Free-shipping invariant** — add to the existing `Quote_WithCompletedJob_ReturnsValidQuote`: any `lego_official` seller with `piece_cost_cents >= 3500` has `shipping_cost_cents == 0`. No new test.
10. **`Status_DeserializesAllSagaStatusValues`** — pure unit test over canned JSON; depends on 0.1.
11. **`OrderList_NonExistentJob_Returns404`** + **`OptimizerPreview_NonExistentJob_Returns404`** — two one-liners.
12. **`Queue_TwoConcurrentJobs_SecondIsQueued`** — submit two `blockWidth=2` jobs, poll `/queue` between, assert the second is in `queued_job_ids`. Closes 2.2/2.3/3.4 together.
13. **`Artifacts_StaticMount_ServesCompletedZip`** — one shape test on `/artifacts/{id}/artifact.zip`; forces an explicit decision on the parallel download surface (1.3).
14. **`Cors_DisallowedOrigin_NoAllowOriginEcho`** — one OPTIONS preflight (3.2).

Items 1–3 fix wrong signals; 4–9 are each <10 lines and close customer-visible contracts; 10–14 are the next tier.

---

## 6. Out-of-scope (intentionally not tested in nightly CI)

Excluded by design; keep so until tooling changes, and keep a manual-runbook trigger for each:

- **Real `/confirm` → saga → marketplace orders / Stripe capture** — moves real money; the terminal `SagaStatus` states (`payment_captured`, `compensated`, `manual_review`) are only reachable here. Verified manually + sandbox runbooks.
- **413 file too large** — `MAX_UPLOAD_SIZE_MB=250`; streaming 250 MB over Render bandwidth is wasteful.
- **429 queue full** — needs ≥21 simultaneous in-flight jobs (`MAX_QUEUE_SIZE=20`); load-test territory.
- **503 server-shutting-down** — only during a real shutdown window.
- **`JOB_TIMEOUT_SECONDS` expiry (1800 s)** and **`JOB_TTL_SECONDS` eviction (600 s)** — wall-clock waits unfriendly to CI.
- **500 disk-write failures** — not remotely triggerable.
