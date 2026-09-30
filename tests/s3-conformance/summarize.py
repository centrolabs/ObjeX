#!/usr/bin/env python3
"""Prints the pass rate of a ceph/s3-tests JUnit report and, with --failures, what still fails in scope."""
import argparse
import re
import sys
import xml.etree.ElementTree as ET

# Tests that carry no marker but still need something ObjeX leaves out on purpose: ACLs, versioning, object lock,
# CORS, policies, checksums, anonymous access and Signature V2 (the POST Object tests sign with it).
OUT_OF_SCOPE = re.compile(
    r"acl|versioning|versioned|version_id|object_lock|retention|legal_hold|cors|policy|public|block|checksum"
    r"|tagging|lifecycle|website|encrypt|sse|logging|anonymous|_v2$|presigned_get_object_v2|ownership|grant"
    r"|torrent|restore|storage_class|attributes|post_object"
)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--min", type=float, default=0, help="exit 1 when the in-scope rate is below this percentage")
    parser.add_argument("--failures", action="store_true", help="list the in-scope tests that failed")
    args = parser.parse_args()

    root = ET.parse(args.report).getroot()
    suite = root if root.tag == "testsuite" else root.find("testsuite")
    results = {}
    for case in suite.findall("testcase"):
        problem = case.find("failure") if case.find("failure") is not None else case.find("error")
        results[case.get("name")] = None if problem is None else (problem.get("message") or "").split("\n")[0]

    in_scope = {name: problem for name, problem in results.items() if not OUT_OF_SCOPE.search(name)}
    passed = sum(problem is None for problem in results.values())
    passed_in_scope = sum(problem is None for problem in in_scope.values())
    rate = 100 * passed_in_scope / len(in_scope) if in_scope else 0

    print(f"ran       {len(results)}")
    print(f"passed    {passed}")
    print(f"in scope  {passed_in_scope}/{len(in_scope)} = {rate:.0f}%")

    if args.failures:
        for name, problem in sorted(in_scope.items()):
            if problem is not None:
                print(f"  {name}: {problem[:110]}")

    return 1 if rate < args.min else 0


if __name__ == "__main__":
    sys.exit(main())
