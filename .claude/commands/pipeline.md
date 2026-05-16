Run the LAIGO nightly pipeline report to show last night's results and 30-day flakiness trends.

## How to invoke
- `/pipeline` — last 30 days (default)
- `/pipeline --days 7` — last 7 days only
- `/pipeline --no-cache` — force re-download of all TRX artifacts

## Steps

1. Run the script from the project root:
   ```
   python scripts/pipeline_report.py $ARGUMENTS
   ```
   where `$ARGUMENTS` is whatever the user passed after `/pipeline` (e.g. `--days 7`).

2. Present the output to the user. Highlight:
   - **Last run status** — did it pass or fail last night?
   - **Failed tests** — list them with their first error line
   - **Flaky tests** — tests that sometimes pass and sometimes fail (these need investigation)
   - **Consistently failing tests** — tests that have never passed (likely broken or environment issue)

3. If the script exits with an error about `GITHUB_TOKEN`, tell the user:
   - Add `GITHUB_TOKEN=ghp_your_token_here` to the `.env` file in the project root
   - Create a token at https://github.com/settings/tokens with scope `repo`
   - Or a fine-grained PAT on `gjGameJam/AutomationLaigo` with **Actions: Read-only**

4. If any tests are flaky, offer to help investigate: check the test source, look at error messages across runs, and suggest whether the test logic or the server behavior is the likely cause.
