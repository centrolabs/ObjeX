# S3 conformance

Runs [ceph/s3-tests](https://github.com/ceph/s3-tests) against this checkout of ObjeX.

```bash
tests/s3-conformance/run.sh
```

It needs `dotnet`, `python3` and `git`. The script starts its own ObjeX on ports 19000/19001 with throwaway data, runs the suite and prints the pass rate. A run takes about two minutes.

| Option | Effect |
|---|---|
| `FAILURES=1` | Also list the in-scope tests that fail |
| `MIN_PASS_RATE=80` | Exit 1 below that in-scope rate, for CI |
| extra arguments | Go to pytest, for example `-k multipart` |

## Scope

- `excluded-markers.txt` lists the suite's markers for features ObjeX does not implement. Those tests do not run.
- `summarize.py` also leaves tests out of the rate that need ACLs, versioning, object lock, CORS, policies, checksums, anonymous access or Signature V2. They carry no marker, so they run, but do not count.
- The suite is pinned to one commit in `run.sh`. Bump it on purpose; the rate moves with upstream changes.
- Both test users share one credential, so a few tests that need a second user fail.
