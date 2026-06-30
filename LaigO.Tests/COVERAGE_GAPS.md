# LAIGO API Test Coverage Gap Audit

**Date:** 2026-06-29 (supersedes the 2026-05-31 / 06-03 audits)
**Scope:** the **live (mounted)** LAIGO API surface in the backend repo
(`C:\Users\Grant Benson\OneDrive\Desktop\LAIGO`, `scripts/Main.py` + `scripts/pay_router.py`
+ `scripts/checkout/gate_router.py`) vs. `LaigO.Tests`.
**Method:** endpoint-by-endpoint cross-reference of the deployed Python contract
against the C# client + fixtures; every mounted route's documented status branch
matched to a concrete `[Test]`.

Severity legend:
- **P0** — Test gives a wrong signal *right now* (model drift, silent always-pass).
- **P1** — A live endpoint or major branch is entirely unverified; customer-visible.
- **P2** — Validation / edge case unverified; lower blast radius.
- **P3** — Defensive/observability paths; nice-to-have.

---

## ⚠️ What changed since the last audit (2026-06) — read this first

The backend's commerce architecture was **replaced**, and the test docs/fixtures
had not caught up. Verified against current `scripts/`:

1. **The checkout saga is SHELVED and unmounted.** `Main.py:63-66` documents it:
   the `checkout.router` (quote / confirm / status) and `debug_router`
   (`/checkout-debug/*` LEGO/BrickOwl sourcing + `/optimize`) "stays on disk but is
   no longer imported or mounted." `Main.py:502-505` mounts only `pay_router`,
   `webhook_router`, `donate_router`, and `checkout_gate_router`. So every
   `/jobs/{id}/checkout/quote|confirm|status` and `/checkout-debug/*` path now
   **404s at the router level** — those endpoints are gone, not merely gated.

2. **The build pack is now a pay-what-you-want digital product** served by
   `scripts/pay_router.py`. Three **new, live, and entirely untested** endpoints:
   - `POST /jobs/{job_id}/pay` — name-your-price charge (or free download).
   - `POST /donate` — standalone tip (client-confirm via Stripe.js).
   - `POST /webhooks/stripe` — authoritative payment recorder.
   This is the headline gap: **§2 below.**

3. **`GET /checkout/gate` is still mounted** (`gate_router`) and still returns 200
   with `mode="disabled"` on the current JSON/checkout-off deployment.

4. **Storage is JSON, not Postgres/Neon.** `Main.py:274-289` *refuses to boot* with
   `CHECKOUT_ENABLED=true` unless `DB_BACKEND=postgres`, so on the live JSON instance
   checkout is necessarily off. Any Postgres-only contract (e.g.
   `ACTIVE_CHECKOUT_EXISTS`) cannot be exercised against this deployment.

5. **Test fixtures for the shelved surface are now `[Ignore]`d**, not deleted (so
   they're trivially re-enabled if checkout returns). See §4. They report *Skipped*.

**Net effect on this document:** the entire old "checkout / quote / optimizer /
sourcing" gap analysis is moot while that pipeline is shelved. It is preserved in
git history; do not re-open those items. The real open gap is the pay path (§2).

---

## 1. Live API surface — current coverage map

| Method | Path | Mounted? | Test fixture(s) | Coverage |
|---|---|---|---|---|
| GET | `/health` | ✅ | `HealthTests`, `HttpSemanticsTests` | **Good** — 200/shape + HEAD/PUT 405 |
| GET | `/` | ✅ | `HealthTests` | **Good** |
| GET | `/queue` | ✅ | `QueueTests`, `QueueConcurrencyTests` | **Good** — structure + FIFO ordering |
| POST | `/generate` | ✅ | `GenerateValidationTests`, `GenerateLifecycleTests` | **Good** — value-range 422s, 400 image, 2d/3d/toFrame happy paths, artifact ZIP |
| GET | `/jobs/{id}` | ✅ | `JobLookupTests`, `GenerateLifecycleTests`, `QueueConcurrencyTests` | **Partial** — 404, complete shape, mid-flight shape, no-cache, queued-position. Gaps in §3. |
| GET | `/jobs/{id}/preview` | ✅ | `JobLookupTests`, `GenerateLifecycleTests` | **Good** — 404 `PREVIEW_NOT_AVAILABLE` + 200 JSON object |
| GET | `/jobs/{id}/download` | ✅ | `JobLookupTests`, `GenerateLifecycleTests` | **Good** — 404 + 200 ZIP + lifecycle 404→200 |
| GET | `/artifacts/{id}/artifact.zip` | ✅ | `ArtifactStaticMountTests` | **Good** — 200 ZIP + 404 |
| (CORS) | preflight on `/health` | ✅ | `CorsTests` | **Good** — allow-list echo / no-echo |
| **POST** | **`/jobs/{id}/pay`** | ✅ | **none** | **❌ ZERO — §2.1** |
| **POST** | **`/donate`** | ✅ | **none** | **❌ ZERO — §2.2** |
| **POST** | **`/webhooks/stripe`** | ✅ | **none** | **❌ ZERO — §2.3** |
| GET | `/checkout/gate` | ✅ | `CheckoutGateTests` | **`[Ignore]`d** (shelved-by-policy; endpoint still live) — §4 |
| POST | `/jobs/{id}/checkout/quote` | ❌ unmounted | `QuoteValidationTests`, `QuoteTests` | `[Ignore]`d; endpoint gone — §4 |
| POST | `/jobs/{id}/checkout/confirm` | ❌ unmounted | `CheckoutConfirmTests` | `[Ignore]`d; endpoint gone — §4 |
| GET | `/jobs/{id}/checkout/{cid}/status` | ❌ unmounted | `CheckoutStatusTests` | `[Ignore]`d; endpoint gone — §4 |
| GET/POST | `/checkout-debug/*` | ❌ unmounted | `DebugLookupTests`, `DebugValidationTests`, `OptimizerPreviewTests`, `LegoSourcingCanaryTests` | `[Ignore]`d; endpoints gone — §4 |

The mosaic pipeline (generate → jobs → download/preview/artifacts) is well covered.
The new commerce path is the gap.

---

## 2. P1 — The pay-what-you-want commerce path is entirely untested

`scripts/pay_router.py` is mounted (`Main.py:502-504`) but has **no client methods
in `LaigOApiClient` and no fixtures**. Adding coverage requires new client helpers
(`PayRawAsync`, `DonateRawAsync`, `StripeWebhookRawAsync`) plus the tests below.
Several branches are pure-read / no-money and CI-safe; the actual-charge paths move
real money and stay out of scope (§6).

Key ordering note for assertion authors: **Pydantic body validation (422) runs
before the handler**, so a malformed body 422s even against a non-existent job.
Within the handler the order is: `INVALID_JOB_ID` (400) → `JOB_NOT_FOUND` (404) →
free (200) → `AMOUNT_BELOW_MINIMUM` (422) → `PAYMENT_METHOD_REQUIRED` (422) →
provider charge.

### 2.1 `POST /jobs/{job_id}/pay` (`pay_router.py:120-239`)

`PayRequest`: `amount_cents: int` (`ge=0`, `le=99_999_999`, required);
`payment_method_id: str | None`. Mounted with prefix `/jobs` → `/jobs/{id}/pay`.

| Branch | Trigger | `detail.code` | CI-safe? | Job needed? |
|---|---|---|---|---|
| 422 (Pydantic) | `amount_cents` missing / non-int / `<0` / `>99999999` | (Pydantic list) | **Yes** | No |
| 400 | `job_id` fails `^[A-Za-z0-9_-]{1,128}$` (e.g. `bad.id`) | `INVALID_JOB_ID` | **Yes** | No |
| 404 | valid-charset job with no `artifact.zip` (use `NonExistentJobId`) | `JOB_NOT_FOUND` | **Yes** | No |
| 200 | `amount_cents=0` on a completed job → `{status:"free", amount_cents:0}` | — | **Yes** (writes `payment.json`, harmless) | **Completed** |
| 422 | `1 ≤ amount_cents ≤ 49` on a completed job | `AMOUNT_BELOW_MINIMUM` (+`min_cents:50`) | **Yes** | **Completed** |
| 422 | `amount_cents ≥ 50`, no `payment_method_id`, completed job | `PAYMENT_METHOD_REQUIRED` | **Yes** | **Completed** |
| 503 | no payment provider registered | `PAYMENTS_UNAVAILABLE` | depends on instance Stripe config | — |
| 503 | transient Stripe error | `PAYMENT_RETRYABLE` | needs real charge | — |
| 402 | card declined | `PAYMENT_FAILED` | needs real card | — |
| 200 | 3DS challenge → `{status:"requires_action", client_secret, payment_intent_id}` | — | needs real card | — |
| 200 | success → `{status:"paid", amount_cents, payment_intent_id}` | — | **real money — §6** | — |

**Recommended tests (all CI-safe):**
- `Pay_InvalidJobId_Returns400WithCode`
- `Pay_NonExistentJob_Returns404WithCode`
- `Pay_NegativeAmount_Returns422` and `Pay_AmountAboveMax_Returns422` (Pydantic, no job)
- `Pay_CompletedJobBelowMinimum_Returns422WithCode` (completed job + `amount_cents=25`)
- `Pay_CompletedJobPaidWithoutMethod_Returns422WithCode` (completed job + `amount_cents=100`, no `payment_method_id`)
- `Pay_CompletedJobFreeDownload_Returns200` (completed job + `amount_cents=0`; assert `status=="free"`)

### 2.2 `POST /donate` (`pay_router.py:271-334`)

`DonateRequest`: `amount_cents: int` (required, no Field bounds — range enforced by an
explicit check); `job_id: str | None`. Mounted at app root → `/donate`.

| Branch | Trigger | `detail.code` | CI-safe? |
|---|---|---|---|
| 422 (Pydantic) | `amount_cents` missing / non-int | (Pydantic list) | **Yes** |
| 400 | `amount_cents < 50` or `> 99999999` | `INVALID_AMOUNT` (+`min_cents:50`) | **Yes** (check precedes provider) |
| 503 | provider unavailable / transient | `PAYMENTS_UNAVAILABLE` / `PAYMENT_RETRYABLE` | provider-dependent |
| 500 | permanent provider error | `PAYMENT_ERROR` | — |
| 200 | `{client_secret}` (mints an **unconfirmed, uncharged** PaymentIntent) | — | creates a Stripe object; provider-dependent |

**Recommended tests (CI-safe):**
- `Donate_BelowMinimum_Returns400WithCode` (`amount_cents=25`)
- `Donate_MissingAmount_Returns422` (Pydantic)

Note: unlike `/pay`, `0` is **not** free here — `/donate` rejects anything `< 50`.

### 2.3 `POST /webhooks/stripe` (`pay_router.py:350-399`)

Mounted at app root → `/webhooks/stripe`. Verifies a `stripe-signature` header
against `STRIPE_WEBHOOK_SECRET`.

| Branch | Trigger | `detail.code` | CI-safe? |
|---|---|---|---|
| 503 | `STRIPE_WEBHOOK_SECRET` unset | `WEBHOOK_NOT_CONFIGURED` | **Yes** |
| 400 | missing / invalid signature | `INVALID_SIGNATURE` | **Yes** |
| 200 | validly-signed event → `{received:true}` (records `payment.json` on `payment_intent.succeeded` w/ `job_id` metadata) | — | **No** — can't forge a valid signature without the secret |

**Recommended test (CI-safe, honest):**
- `StripeWebhook_UnsignedPayload_RejectsWithStructuredCode` — POST an unsigned body;
  assert status ∈ {400, 503} **and** `detail.code` ∈ {`INVALID_SIGNATURE`,
  `WEBHOOK_NOT_CONFIGURED`}. This proves the endpoint exists, is signature-gated, and
  never accepts an unsigned payload. The 200 recording path is unreachable in CI by
  design (no secret) — do not fake it.

---

## 3. Remaining gaps on the core mosaic pipeline (P2/P3)

These survive from the prior audit and are still real on the live endpoints:

### 3.1 `GET /jobs/{job_id}` — non-complete status branches
Covered: 404, `complete` shape, mid-flight shape, `queued` with `queue_position`/
`queue_length` (via `QueueConcurrencyTests`), no-cache header. Still untested:
- `status=running` with `progress` strictly in `[0,100)` mid-flight (cheap: submit, poll once before completion).
- A **real** `failed` job with `error` populated — no test deterministically produces one (the invalid-image 400 is rejected at `/generate`, before a job exists). P3.
- `timed_out` normalized to `failed` (impractical — `JOB_TIMEOUT_SECONDS=1800`). §6.
- Eviction after `JOB_TTL_SECONDS` (600) → 404 (slow). §6.

Field-name reminders: the response uses **`finished_at`** (not `completed_at`),
`created_at == queued_at`, and `progress` is forced to `100` on complete / `0` on failed.

### 3.2 `POST /generate` — uncovered defensive branches
Value-range 422s, the 400 invalid-image branch, and 2d/3d/toFrame happy paths are
covered. Untested (all impractical in CI — see §6): 503 shutting-down, 429 queue-full
(needs ≥21 in-flight, `MAX_QUEUE_SIZE=20`), 413 too-large (`MAX_UPLOAD_SIZE_MB=250`),
500 disk failure.

### 3.3 `GET /jobs/{id}/preview` — 500 branch
200 + 404 (`PREVIEW_NOT_AVAILABLE`) covered. The 500 `PREVIEW_CORRUPTED` branch
(unreadable `preview.json`) is not remotely triggerable. P3.

### 3.4 Cross-cutting (P2/P3)
- **Global exception handler** (`{detail, request_id}`, no exception text leaked) — no reliable trigger; operator-log-observable. P2.
- **X-Forwarded-For → `request.state.real_ip`** — not surfaced via any API; untestable from C# until an audit-fetch endpoint ships. P2 placeholder.
- **Trailing-slash / method-not-allowed** on the live routes beyond `/health` (e.g. `DELETE /jobs/{id}`) — untested. P3.

---

## 4. Shelved surface — fixtures `[Ignore]`d, not deleted

The checkout/quote/sourcing feature is shelved (2026-06). Its fixtures carry a
class-level `[Ignore(...)]` and report *Skipped* — never run, never fail, in CI or
locally. They stay in-tree so re-enabling is just deleting the `[Ignore]` line.
**Two sub-groups, with an important distinction:**

- **`/checkout/gate` is still mounted.** `CheckoutGateTests` (8) is ignored *by policy*
  (the owner shelved the whole checkout area) — but the endpoint is live, so these
  would pass if un-ignored.
- **Everything else targets unmounted endpoints** (would 404 if run):
  `QuoteValidationTests`, `QuoteTests`, `CheckoutConfirmTests`, `CheckoutStatusTests`
  (pure-unit saga-model round-trips, no network — but for a shelved model),
  `DebugLookupTests`, `DebugValidationTests`, `OptimizerPreviewTests`, and the
  `Quarantine/LegoSourcingCanaryTests`.

If checkout returns, re-enabling these also requires re-mounting the routers in the
backend **and** re-checking each contract — the saga predates the L5 provider rename
and the Postgres-only `ACTIVE_CHECKOUT_EXISTS` path. See `TESTING_GUIDELINES.md` §5.

---

## 5. Recommended additions, ordered by ROI

Highest-impact, lowest-cost first. The pay path is the only live-functionality gap.

1. **Add `LaigOApiClient` helpers** for `/jobs/{id}/pay`, `/donate`, `/webhooks/stripe`
   (raw-response variants; the typed models can come later). Prerequisite for 2–5.
2. **`Pay_*` no-job 422/400/404** (§2.1: invalid job id, non-existent job, Pydantic
   amount bounds) — pure-read, no money, no job.
3. **`StripeWebhook_UnsignedPayload_RejectsWithStructuredCode`** (§2.3) — one request,
   proves the webhook is signature-gated.
4. **`Donate_BelowMinimum_Returns400WithCode` + `Donate_MissingAmount_Returns422`** (§2.2).
5. **`Pay_CompletedJob*` (free / below-minimum / paid-without-method)** (§2.1) — each
   needs a `SubmitAndAwaitCompletionAsync` job; put them in `Pipeline/` with a new
   `[Category("Pay")]`.
6. **`JobStatus_Running_ProgressInRange`** (§3.1) — submit, poll once mid-flight.

A new area category **`Pay`** keeps the live commerce path filterable and separate
from the shelved `Checkout` set.

---

## 6. Out-of-scope (intentionally not tested in nightly CI)

Excluded by design; keep a manual-runbook trigger for each:

- **A real successful `/pay` charge** (`status:"paid"`) — moves real money; needs a
  real `payment_method_id` + live/test Stripe key. Sandbox runbook only.
- **`/pay` 402 declined / 503 retryable / 200 requires_action** — need a real Stripe
  round-trip (test cards). Manual / sandbox.
- **`/donate` 200 `client_secret`** — mints a real Stripe PaymentIntent; provider-dependent.
- **`/webhooks/stripe` 200 recording path** — requires a validly-signed event (needs
  `STRIPE_WEBHOOK_SECRET`); use Stripe's CLI/event-replay in a sandbox.
- **`/generate` 413 / 429 / 503 / 500** — load-test / shutdown / disk-failure territory.
- **`JOB_TIMEOUT_SECONDS` (1800 s) expiry, `JOB_TTL_SECONDS` (600 s) eviction** —
  wall-clock waits unfriendly to CI.
- **The shelved checkout saga's terminal money states** — moot while unmounted (§4).
