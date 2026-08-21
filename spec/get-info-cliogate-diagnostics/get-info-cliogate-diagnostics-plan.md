# get-info cliogate diagnostics - PLAN

> GitHub: [#1138](https://github.com/Advance-Technologies-Foundation/clio/issues/1138)

## Decision

Move the existing single-attempt, recoverable `GetSysInfo` call ahead of package-version
classification. When it succeeds, merge the report and stop. When it fails, call
`IClioGateway.GetInstalledVersion()` solely to choose an accurate warning.

This keeps the change inside `GetCreatioInfoCommand` and leaves the shared lowest-alias policy
unchanged for commands whose package dependency is a hard prerequisite.

## State mapping

1. `GetSysInfo` succeeds: report cliogate data, no warning.
2. Probe fails and no version is detected: cliogate is not installed.
3. Probe fails and the lowest detected alias is below 2.0.0.32: name that alias version and floor
   without claiming it is the active runtime package.
4. Probe fails and detected version meets the floor: name the version and identify the
   `GetSysInfo`/permission boundary.
5. Probe and version detection fail: preserve the base report and say detection was inconclusive.

## Trade-off

Gate-less environments now make one bounded `GetSysInfo` request before package detection. This is
accepted because endpoint capability is the only reliable answer when installed aliases disagree.
