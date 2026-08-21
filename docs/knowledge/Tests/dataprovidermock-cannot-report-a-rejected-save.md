---
description: ATF.Repository's DataProviderMock has no failing-save mode, so a throw-on-Save-failure contract needs a hand-written IDataProvider decorator whose BatchExecute returns an unsuccessful IExecuteResponse - see RejectingSaveDataProvider in FeatureStateServiceTests
applies-to:
  - clio.tests/Command/FeatureStateServiceTests.cs
  - clio/Command/FeatureStateService.cs
ticket: ENG-93848
date: 2026-08-19
---

**What is true** — `DataProviderMock` covers reads and writes (`MockItems`, `MockSavingItem` with
`ReceivedCount` / `ChangedValueHas`) but every save it observes succeeds. A code path that throws when
`IAppDataContext.Save()` returns `Success == false` is therefore unreachable through the mock alone.
`FeatureStateServiceTests.RejectingSaveDataProvider` is the pattern: a small `IDataProvider` that
forwards `GetItems` / `GetDefaultValues` to a real `DataProviderMock` and returns a failing
`IExecuteResponse` from `BatchExecute`.

**Why it is this way** — the mock is a happy-path fixture from the ATF.Repository package; failure
injection is not part of its surface and cannot be added from this repository.

**What breaks if you ignore it** — the rejected-save branch reads as covered because the fixture around
it is green, and deleting the `throw` leaves the whole suite passing. That branch is the only thing
standing between a rejected platform write and a command that reports success, so it needs a test that
can actually reach it. A second lever the same fixture relies on: `MockItems` is keyed by schema name,
so two ATF projections over the same physical row (`AdminUnitFeatureState` for reading,
`AppFeatureState` for writing) are mocked independently - populate the first and leave the second empty
to reach the "found by the read projection, not re-readable through the writable one" branch.
