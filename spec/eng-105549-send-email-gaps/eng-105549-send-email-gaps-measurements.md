# ENG-105549 Send email gaps: stand measurements

Stand: the shared test stand (`.NET Framework`), CrtProcessBuilder 1.6.6.137 installed (another task's build; no install
was made for these measurements). Every object created is named `UsrSe105549*`, in package `Custom`. Raw reads are in
the lead's local research notes, not in the repository.

## M6 `ignoreErrors` (2026-11-04)

Automatic Send email, custom message, `ignoreErrors = true`, one run each.

| Case | Setup | Result |
|---|---|---|
| a | no sender, one constant recipient | process FAILED: `EmailException: Sender is not specified`, thrown from `AutoEmailUserTaskSender.SendEmailWithDefaultSender` |
| b | sender bound to a `MailboxSyncSettings` id that has no record | process FAILED, same stack: the sender resolves to an empty From and takes the default-sender path |
| c | no sender, no recipient | process COMPLETED |

Agrees with CrtProcessDesigner 7.8.0 `AutoEmailUserTaskSender.Execute`: an empty recipient list returns early when
`IgnoreErrors` is on; an empty From goes to `SendEmailWithDefaultSender`, which (current email integration) does not
consult `IgnoreErrors`; a resolved From goes through `TrySendEmailWithRetry`, which swallows the send error when
`IgnoreErrors` is on. **Not measured:** a resolved sender whose send fails (it would need breaking the stand's only
mailbox, shared with another task) - source only.

**Consequence for the guide:** `ignoreErrors` does not keep the process alive when the email has no usable sender (none
set, or one that names no mailbox); it does when there is no recipient and, per the source, when a configured mailbox
fails to send.

## M8 recipients after a designer save (2026-11-04)

Manual Send email with three recipients: a fixed contact (`expression` `[#Lookup.<Contact>.<id>#]` + `referenceSchema`
Contact: Lookup, Script), a Contact-lookup process parameter (Lookup, Script meta path), a constant address (MaxSizeText,
ConstValue).

1. Element opened in the Process Designer, saved: all three keep type, source and value; the designer adds
   `valueDisplay` only (and materialises the manual-mode inherited parameters).
2. The same three entries sent again through `setElement` `email.to`: no recipient added.
3. `addMapping` onto `Recipient2` with a contact id (the replacement route, behaviour 5): replaced in place.
4. Designer save again: `Recipient2` becomes type **MaxSizeText**, still source ConstValue, value the contact id,
   `referenceSchema` Contact, display the contact name.

**Conclusion:** the designer rewrites the TYPE of a constant-contact recipient (the shape `addMapping` with a contact id
writes; the `email` block never writes it) and keeps its source, value and reference. Recipient matching compares source
and value only, and describe reports the stored type with the same value and display, so neither depends on the type:
no code change; a package test pins the type-independent match.

### M8b run time of the retyped recipient (2026-11-04)

The same process, after step 4, run once (manual mode, so nothing is sent; the prepared email activity shows the
addresses the run resolved). The activity's recipient field read
`<Supervisor's email>;<the bare contact id>;nobody@example.invalid`: the Script `[#Lookup...#]` recipient resolved to
the contact's email, while the retyped constant (MaxSizeText, ConstValue = contact id) went out as the literal id.

**Consequence:** replacing a contact recipient through `addMapping` with a bare contact id works only until the next
designer save, which silently turns it into an undeliverable address. The `[#Lookup.<Contact>.<id>#]` expression form
survives the save (step 1) and resolves (this run).

## Created objects (cleanup on the owner's word)

Processes `UsrSe105549IgnoreNoSender`, `UsrSe105549IgnoreBadSender`, `UsrSe105549IgnoreNoRecipient`,
`UsrSe105549RecipientType` (package Custom), the three process-log rows of the M6 runs, and the M8b run (a running
process instance waiting on its manual email activity "M8 recipients").
