# Bugs

No confirmed bugs at this time. All previously identified issues have been resolved.

## Resolved

- **TreeView expansion state** — `Projects.Clear()` was called before reading expansion state. Fixed by swapping the two lines.
- **Images/Volumes refresh hang** — missing timeout and `try/finally` for `_isRefreshing` flag reset. Fixed by adding `CancellationTokenSource` with 15s timeout and `try/finally`.
- **Podman silent JSON parse failure** — bare `catch {}` swallowed all exceptions. Fixed by adding `Runner.LogWarning` with the exception.
