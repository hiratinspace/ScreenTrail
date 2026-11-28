# Security questionnaire: pre-filled answers

For procurement and vendor-review forms. Every answer names what enforces it, so a reviewer can check
rather than take our word for it.

**Answers marked ⚠ describe something not yet built.** Brought into line with `main` on 2026-09-28
(after #168); the review by an MSP security lead (7.4) has still not happened. They are written as "designed to" rather than
"does", because a questionnaire that overstates a shipped control is the one document that will be held
against you later. Do not delete the marks before the tickets close.

Status as of 2026-09-22: the Windows client is built and its UI is connected to the capture service; the
backend is built and runs locally. Publishing, hosting, enrolment, tenant policy and the Settings screen
are not built. This pack has not had a security-lead review (7.4).

---

## 1 · Data collection

**1.1 What data does the product collect?**
Screenshots of the technician's screen during a remote-support session, a timeline of input *categories*
(not keys), the technician's spoken narration as text, and a drafted ticket note.

**1.2 Does it record keystrokes?**
No. The keyboard hook emits categories and counts (a typing burst of *n* characters, a shortcut, Enter),
and no field in the data format can hold a key or character. Enforced by the hook and independently by the
schema (`additionalProperties: false`), so a future change cannot add one quietly. *(INV-2)*

**1.3 Does it record the customer's voice?**
No. Only the technician's microphone. There is no code path that produces any other speaker. *(INV-9)*

**1.4 Does it record video?**
No. Still screenshots, taken on click or on significant screen change.

**1.5 How often are screenshots taken?**
On technician clicks (debounced) and on scene change, with a per-session budget. Not on a timer and not
continuously.

**1.6 Which windows are captured?**
By default, the remote-support tool plus an allowlist of admin tools. Capturing all windows is opt-in and
warns that other customers' data may be on screen. The desktop, taskbar and Task View are never captured
in either mode, because a picture of the desktop is a picture of every window on screen. *(INV-5)*

**1.7 Can capture be running without the user knowing?**
No. A recording pill is on screen whenever capture is anything but idle, it cannot be dismissed
mid-session, and a tray icon shows the same state. The UI reports the pill to the service every two
seconds with its position, and the service treats a pill it has not heard from in six seconds as absent,
so a UI that has stopped painting does not count as an indicator. **While no indicator is reported, the
service suppresses capture** after a short grace for UI restarts: no frame, no typing count, no
transcript is written, and the interval is in the audit log. *(INV-4. Between 2026-09-15 and
2026-09-16 this answer was "yes": the indicator existed and nothing showed it. Closed by ST-085; the
active report was added 2026-09-22.)*

**1.8 Is the indicator visible to the customer during screen sharing?**
No. The indicator sets `WDA_EXCLUDEFROMCAPTURE`, verified on the hosted runner and on the laptop
(ADR-0001 AC3), so a screen-share viewer does not see it while the technician does.

**1.9 What happens when a password field has focus?**
On the technician's own machine, capture stops. No screenshot and no typing event is recorded for that
interval: the data is dropped, not hidden. Detection is by UI Automation, verified on real hardware.
*(INV-6)* It does not cover a password field on the customer's machine seen through the remote-support
tool: the technician's machine sees only the tool's picture of it, so capture goes on, and what stands in
for it there is the sign-in screen check (which reads the words on screen) and the masking of a value beside
a password label (2.5). A local application that does not answer UI Automation in time can be missed too.

**1.10 What other conditions stop capture?**
An excluded application in front, a window running elevated, or a screen that matches sensitive-context
heuristics. All drop data the same way. The technician's speech follows the screenshots: while an excluded,
elevated or out-of-scope window is in front, nothing said is transcribed or stored.

---

## 2 · Data handling on the device

**2.1 Where is data stored?**
An encrypted SQLite database (SQLCipher) in the technician's own user profile.

**2.2 How is the encryption key protected?**
Windows DPAPI, scoped to that user account.

**2.3 Can a screenshot be read before it is redacted?**
No. A captured frame waits in memory and is written to the store only after redaction (ADR-0006); every
read path also filters on the pending flag, as a second line. A test reads the stored bytes back to
prove nothing unredacted is ever there. *(INV-1)*

**2.4 What if redaction fails on a frame?**
The frame is deleted and counted. The same happens to a frame captured while the redaction queue is full.
The count reaches the drafted note, which then says it did not see everything rather than pretending it
did. An unreadable frame retained is an unredacted frame. A frame the OCR engine read only in part is not
treated as a failure: it is kept, and the text the engine did not read on it is not masked (2.5).

**2.5 What is redacted?**
The rules are written for the United States. Card numbers (Luhn-checked), United States social security
numbers and ITINs, API keys and tokens, private key blocks, BitLocker recovery keys, an authenticator's
setup key and `otpauth://` secret, password fields, passwords beside a password label (in English and in
the other languages the sign-in check knows), every cell of a table's Password column, a password in a
web address, a Wi-Fi key as Windows lists it, the password on a `net use`, `net user`, `psexec -p`,
`cmdkey`, `-Password`, `--password` or `ConvertTo-SecureString` command line (after `net use`, `net
user` and `psexec -p` only when it looks like a password), and any patterns the technician adds in
Settings (the tenant's policy does not carry patterns yet). In an installed copy the four built-in
switches (cards, social security numbers, API keys, passwords) cannot be switched off; until the
tenant's policy can set which rules are required, that is the rule for every installed device. Built and
not switched on until measured on real screens: Medicare, passport, driver's licence, bank routing and
bank account numbers beside their label. Not recognised: names, addresses, dates of birth, phone numbers
and other free-form personal data, other countries' national ID numbers (a UK National Insurance number,
for one), IBANs, password hashes and hexadecimal keys with no label, MFA backup codes, Windows product
keys, a card number said as words, and anything that is a picture rather than text. Such a value stays
readable in the screenshot and in its text, including in what is sent for drafting (3.1). Masking
happens on the device before any frame becomes readable.
On a screenshot, a password masks its cue's row and every row it touches, the cue included; only the
words wholly left of the cue stay, and only when the secret is right of it. Every other match takes the whole of each on-screen token it
touches, however the OCR engine split it. A value wrapped onto the next row by its window's edge is
joined to each line that may continue it (up to four, on the three rows below) and masked on both rows;
this masks more than a page needs where unrelated rows happen to complete a pattern between them.
Masking covers only text the on-device OCR engine read. Small or scaled-down text, as in a remote viewer
shrunk to fit, is read less well: on test pages shown through a resizing viewer, the secrets not masked
whole were mostly at 0.6 and 0.7 of their size (the data-flow document gives the figures). Pictures are
not read, so a QR code is never masked.

**2.6 Does redaction run locally or in the cloud?**
Locally, always. OCR uses the Windows on-device engine.

**2.7 How long is raw data kept?**
Default 7 days, configurable 1–30. Screenshots, transcript and timeline are deleted on that clock; the
drafted note survives. *(INV-12)*

**2.8 Is there a delete-everything option?**
Yes: Settings → Privacy & Redaction → Your data → "Delete everything on this device…" (ST-081). The
service accepts the command only with a typed confirmation it issued moments before ("DELETE
EVERYTHING"), and it removes the device's tokens as well as its data: everything ScreenTrail keeps under
`%LOCALAPPDATA%\ScreenTrail`, which is the encrypted store and its key, the device's refresh token, the
settings, the cached policy and the crash reports. Only the downloaded speech models stay, since they
hold nothing about anybody, and the program's own files if it was installed inside that folder. The
device has to be activated again afterwards; its seat stays counted on the backend until an admin revokes
it. The same screen exports the audit log with its chain verification.

**2.9 Is deleted data recoverable?**
Not through ScreenTrail, and not from the database file once SQLite has written the change into it.
Deletion is a hard delete with SQLite's secure delete on, so freed pages are overwritten; the file is
vacuumed after a retention pass that freed enough of it, not after each delete. A blurred region is
flattened to solid colour and the original bytes are overwritten in the same transaction, and no copy is
kept for undo. The text read from that screenshot is deleted in the same statement, so it is not shown in
Review or sent with a later draft either. ⚠ One gap: the store writes through a write-ahead log
(`store.db-wal`), and until that log is next folded into the database, which can be days on a quiet
machine, the database file can still hold the deleted or pre-blur screenshot, and the log can keep the
earlier pages after that. Both files are encrypted like the rest of the store, so reading them needs the
technician's Windows account.

**2.10 Can a technician delete an individual screenshot?**
Yes, with a five-second undo. After that it is permanent and an audit row records it.

---

## 3 · Data leaving the device

**3.1 What leaves the device?**
Only a redacted bundle sent to produce the draft note, and only what the technician publishes. The bundle is
sent by itself when the session ends, about 30 seconds after it and before the technician opens Review; in
local-only mode (3.3) it is not sent. Review governs what is published to the ticket, not what the drafting
model saw: a screenshot the technician excludes or blurs in Review may already have been sent for drafting, in
its redacted form. ⚠ A tenant setting to hold the draft until the screenshots have been reviewed is planned and
not built.

**3.2 Can the product publish automatically?**
No. There is no automatic publishing path. *(INV-3)*

**3.3 ⚠ Is there a mode where nothing leaves the device?**
Yes: with no backend configured, nothing leaves the device. Every HTTP call the service makes goes
through an allowlist that defaults closed, refuses anything that is not HTTPS, and takes the
destination's purpose from the calling code rather than inferring it from the URL. *(INV-8)* A session
in that mode keeps its screenshots and transcript and gets a plainer draft written on the device from
them (no model runs; the note is marked local and Review says so).

**3.4 ⚠ Can an administrator lock local-only mode on?**
Yes. The tenant's policy is fetched at start and hourly under its own egress purpose (the one request
local-only mode makes on its own, with the device sign-in it needs, because it is how local-only is
lifted), the last copy is kept on disk for when the backend is away, and a locked local-only is applied to the egress guard itself rather than only
shown: Settings renders the toggle read-only, at the value the admin set, with "Set by your admin
(policy …)". A policy that arrives with a field missing or of the wrong type is refused and the last
synced one stands, so a lock is never read as "unlocked" because a field name drifted; the wire shape is
`shared/contracts/tenant-policy.v1.json`, tested from both ends. *(INV-11; ST-047, 2026-09-25; the
contract and the value shown (ST-081) 2026-10-14.)* The backend holds the lock as well, and that is the
control that holds against the technician's own account: it refuses to draft for a tenant whose policy
locks local-only, whatever the device believes, so a device that has not fetched the policy, or whose
policy fetch was intercepted (a per-user proxy setting or a root certificate trusted for that user alone
can do that), drafts the session on its own instead. An installed client takes its backend address only
from a machine-wide setting an administrator writes, which is defence in depth: it keeps the address out
of the technician's reach, but it does not by itself stop the fetch being intercepted on the way.
*(2026-11-15.)* The operator sets a policy with
`--set-policy` until the web admin exists (ST-099). It changes only the fields it names, and lifting a
locked local-only takes an explicit `--confirm-unlock` *(security review D-02, 2026-11-28)*.

⚠ The policy has no redaction fields yet: it cannot require a rule on or add a pattern. In the meantime an
installed copy keeps the four built-in rules on whatever its settings say; a change to any redaction
switch, or one that takes a pattern, an excluded app or local-only away, is refused by the service
unless it comes with a one-use confirmation from the same window; and a settings file that breaks the
rules or cannot be read is applied at its strictest (local-only on, every rule on), never as the defaults.
*(Security review A-03, DEC-06, 2026-11-28.)*

**3.5 ⚠ Does the backend store screenshots?**
No. There is no table that could hold a frame, a transcript or a note; a bundle is held in memory for one
request and let go, and the backend's tests count every row in every table before and after a draft.
*(INV-7. The backend is built and runs locally; it is not yet deployed: ST-007.)*

**3.6 What is the hosted model provider?**
Whichever one the deployment is configured with, and only that one: `Summarization:Provider` names it, and
the key it is given belongs to it.

- **Anthropic (Claude)** when the setting is `claude`: `claude-opus-5-5` by default, through Anthropic's
  Messages API at `api.anthropic.com`, under the operator's own Anthropic account and its data-retention
  terms. Anthropic is then the sub-processor for drafting.
- **Google (Gemini Flash)** when it is `gemini-flash`, the default: the Generative Language API, US
  region, with frames sent at low resolution. Google is then the sub-processor for drafting.

What is sent is the same either way: one request per session (two when the first answer breaks a rule
and is sent back to be corrected), carrying the screenshots after redaction (sensitive regions painted
over on the device), the text read from them after masking, and the masked transcript, with the session's
ids and timings. Nothing unredacted is ever in the bundle to send (INV-1). With Anthropic, a session
its safety checks decline may be run again on a second Anthropic model (Claude Opus 5 or Opus 4.8, chosen by Anthropic) inside the same
call, and after such a fallback Anthropic keeps a content-hash routing record, not the content, for about
an hour so the conversation stays on that model (not kept for zero-data-retention organizations). The pictures are sent inline
rather than uploaded, so none is left in the provider's file storage, and the backend keeps nothing
(INV-7). A per-tenant daily spending cap is checked before the call. OpenAI could be added behind the same
interface. *(ST-063; Claude added 2026-10-28.)*

**3.7 Do logs contain customer data?**
No. Logs and metrics have no content-carrying fields: no window titles, OCR text, transcript, note text,
company or ticket names. The diagnostics record has nowhere to put them, which is stronger than scrubbing
them out. *(INV-10)*

**3.8 What does telemetry send?**
Nothing unless the technician switches it on in Settings → Privacy & Redaction, where the exact field
list is shown. One metric per session: `session_id`, `started_at`, `duration_ms`, `frames`,
`transcript_segments`, `frames_purged_unredacted`, `edit_ratio`, `published`; the contract is
`shared/contracts/session-metric.v1.json`, and a test on each side fails if the type ever gains a string
field other than the id. No note text, ticket, company, transcript or window title can be sent, because
there is nowhere in the shape to put one. *(INV-10; ST-098.)*

**3.9 Is the diagnostics panel trustworthy?**
Yes. It is filled from the service's own counters and policy over the pipe; the UI holds no value of its
own to show, and `Local-only` is read from the same object the egress guard enforces with. *(ST-085.)*

---

## 4 · Access and authentication

**4.1 How do the product's components talk to each other?**
A per-user Windows named pipe between the capture service and the UI. No network socket is used, and no
component listens on a TCP port, asserted by a test that fails the build if a listener type appears in
client source.

**4.2 How is that channel authenticated?**
Three checks: a per-user ACL on the pipe, a per-run token left in one file only that user can read, and
verification that the connecting executable is Authenticode-signed by our publisher.

**4.3 Is the signature actually verified, or just present?**
Verified with `WinVerifyTrust`, which hashes the file and walks the chain. An earlier implementation
compared the certificate thumbprint, which a modified binary can satisfy by copying the certificate blob;
that was replaced and there is a test that tampers with a signed binary and asserts refusal.

**4.4 Does the UI verify the service, or only the other way round?**
Both. The UI's connect call **requires** a server verifier rather than defaulting to none, and passes one
that checks the pipe server's executable before the token is written. An unsigned development build
falls back to the same-directory rule on both sides. *(ST-012; wired in ST-085.)*

**4.5 Are debug or diagnostic endpoints present in release builds?**
No. There is no build-conditional code in the client at all, asserted by test: release and debug contain
the same code.

**4.6 ⚠ Are binaries code-signed?**
Designed to be; signing is part of the release pipeline, which is not yet built. Unsigned development
builds fall back to requiring the peer executable to sit in the same directory.

---

## 5 · Auditing

**5.1 Is there an audit log?**
Yes, per session: when it started, was paused, resumed and stopped, and whether the technician or
ScreenTrail started and stopped it; frames captured, frames lost unredacted, suppressed intervals with
reasons and durations, redactions by kind, bytes sent and to which host, each destination a publish
landed in, and every deletion: a screenshot deleted or blurred in Review, a discarded session, a
retention purge. For the device as a whole: each change to a privacy or capture-scope setting saved in
Settings, or made to the settings files while the service was not running and found at its next start
(which setting, and on or off, never its value), and each tenant policy version that took effect. A frame turned away before capture because redaction had fallen behind counts as lost and not as
captured, so the lost count can exceed the captured one.

**5.2 Can the audit log be tampered with?**
Each row carries the hash of the previous row, so removing or editing one breaks every row after it.
`VerifyAuditAsync` walks the chain and reports whether it is intact, how many rows were checked, and the
id of the first row that does not follow from its predecessor. That shows a careless edit, one that does
not rewrite the chain. It is not proof against the technician's own Windows account: that account holds
the store's key, so it can rewrite the log and its chain together, or put back an earlier copy of the
store, and the check would still say intact. ⚠ Nothing off the device keeps the chain's length or last
hash yet, which is what would catch that.

**5.3 Does the audit log contain customer data?**
No. Rows hold a kind, a count, a timestamp and an optional short detail from a fixed vocabulary. There is
no free-text field.

**5.4 Does the audit log survive deletion of the session?**
Yes, deliberately. Discarding a session or purging it on retention keeps the audit rows, so there is a
record that data existed and was destroyed. *(INV-12)* "Delete everything" is the exception: it removes
the store, and the audit log with it, so nothing on the device records that it happened.

**5.5 Can the audit log be exported?**
Yes, JSON or CSV, with the verification result included. The service writes the JSON export to a file in
the same pass that verifies the chain, and the technician chooses where to keep it, anywhere outside
ScreenTrail's own folder. A copy the UI did not collect is removed by the next export, and by "Delete
everything".

---

## 6 · Product behaviour a reviewer should know

**6.1 What does the technician see before anything is published?**
The drafted note, every screenshot with include/exclude control, the transcript, and the timeline. The
drafting request has already gone by then (3.1). Steps
the model inferred rather than observed are marked, and the marker clears only when the technician edits
or confirms them. A screenshot that looked like a sign-in screen, or was taken while capture was paused or
suppressed, is shown marked "withheld": it stays on the device and is never attached or sent, whatever
the technician selects. The service checks this again at publish, against its own record. *(ST-078)*

**6.2 Can the drafted note contain something nobody said?**
The drafting prompt is instructed that OCR text and transcript are evidence rather than instruction, and
post-conditions refuse invented quotations, credentials and instruction-shaped text in every free-text
field. Quotations are checked against the transcript.

**6.3 Could text on a customer's screen influence the note?**
This is treated as a prompt-injection surface. Directives, URLs and command lines are refused in every
field of the draft, not only in the steps.

**6.4 Is the transcript trustworthy?**
Speech recognition can produce fluent text for silence. Segments that look invented (boilerplate phrases
on short or low-confidence audio, or text too long for the audio that carried it) are dropped before
storage, because a hallucinated line would otherwise make a fabricated quotation verifiable.

**6.5 What happens if the machine cannot capture?**
The capability probe reports what Windows allows and the indicator says so. It checks the window station
and session, so a service with no desktop is reported as unable to capture rather than as healthy.

---

## 7 · Compliance and process

**7.1 ⚠ Do you have SOC 2 / ISO 27001?**
No. Not yet pursued.

**7.2 ⚠ Do you carry cyber liability insurance?**
To be answered by the MSP deploying ScreenTrail.

**7.3 Is there a documented threat model?**
Yes: [threat-model.md](threat-model.md), STRIDE, with each threat mapped to the invariant it protects and
the ticket that mitigates it, plus an explicit list of what is *not* mitigated.

**7.4 Has this pack been reviewed by a security professional?**
⚠ **Not yet.** ST-114 requires review by an MSP security lead and that has not happened. Do not represent
this pack as reviewed until it has.

**7.5 Where is consent guidance?**
[consent-guidance.md](../legal/consent-guidance.md). It is explicitly not legal advice and is written to be
taken to counsel.

**7.6 Who do we contact about a vulnerability?**
⚠ To be filled in by the MSP deploying ScreenTrail. A disclosure contact is not yet published.
