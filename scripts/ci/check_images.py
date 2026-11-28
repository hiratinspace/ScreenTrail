#!/usr/bin/env python3
"""Holds every production Dockerfile to images pinned by digest (H-9).

A production image is built from the Dockerfiles under backend/ and deploy/. Each image they start
from, and the build front end a `# syntax=` line would fetch before reading the file, is code that
runs in the build. A tag can be moved to other bytes; a digest cannot. So:

  - a `# syntax=` parser directive, if there is one, names its image with `@sha256:` and a 64-character
    digest (today there is none: the files use nothing the builder's own front end lacks);
  - every `FROM` names an image with `@sha256:` and a 64-character digest, or a stage defined earlier
    in the same file.

The checks are tried first on made-up files that must pass and must fail.

Usage: check_images.py [repo root]
"""

import pathlib
import re
import subprocess
import sys

DIGEST = re.compile(r'@sha256:[0-9a-f]{64}$')
DIRECTIVE = re.compile(r'^#\s*([a-zA-Z]+)\s*=\s*(\S+)\s*$')
FROM = re.compile(r'^\s*FROM\s+(?:--platform=\S+\s+)?(\S+)(?:\s+AS\s+(\S+))?\s*$', re.IGNORECASE)
FOLDERS = ('backend/', 'deploy/')


def problems(path, text):
    bad, stages = [], set()
    lines = text.split('\n')
    # Parser directives are comments at the very top, before any other line (blank lines end them too).
    for line in lines:
        match = DIRECTIVE.match(line)
        if not match:
            break
        if match.group(1).lower() == 'syntax' and not DIGEST.search(match.group(2)):
            bad.append(f"{path}: the syntax directive fetches {match.group(2)!r}, which is not pinned by digest.")
    joined = re.sub(r'\\\n', ' ', text)
    for line in joined.split('\n'):
        if not re.match(r'^\s*FROM\b', line, re.IGNORECASE):
            continue
        match = FROM.match(line)
        if not match:
            bad.append(f"{path}: a FROM line this check cannot read: {line.strip()!r}.")
            continue
        image, stage = match.group(1), match.group(2)
        if image.lower() not in stages and not DIGEST.search(image):
            bad.append(f"{path}: FROM {image!r} is neither pinned by digest nor an earlier stage.")
        if stage:
            stages.add(stage.lower())
    return bad


GOOD = '\n'.join([
    '# A comment that is not a directive.',
    'FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:' + 'a' * 64 + ' AS build',
    'RUN dotnet publish',
    'FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:' + 'b' * 64,
    'COPY --from=build /app /app',
])
STAGED = GOOD + '\nFROM build AS again\n'
MUST_FAIL = {
    'a syntax line by tag': '# syntax=docker/dockerfile:1\n' + GOOD,
    'a syntax line by a short digest': '# syntax=docker/dockerfile:1@sha256:abc\n' + GOOD,
    'a syntax line after another directive': '# escape=`\n# syntax=docker/dockerfile:1.7\n' + GOOD,
    'a syntax line in capitals': '# SYNTAX = docker/dockerfile:1\n' + GOOD,
    'a base by tag': GOOD.replace('@sha256:' + 'a' * 64, ''),
    'a base with no tag': GOOD + '\nFROM alpine\n',
    'a base on a continued line': GOOD + '\nFROM \\\n  alpine:3 AS x\n',
    'a stage used before it is defined': 'FROM later\n' + GOOD + '\nFROM x AS later\n',
    'a base from a build argument': 'ARG BASE=alpine\n' + GOOD + '\nFROM ${BASE}\n',
    'a lower-case from by tag': GOOD + '\nfrom alpine:3\n',
}


def self_test():
    for label, text in (('the good sample', GOOD), ('a later stage', STAGED), ('a pinned syntax line', '# syntax=docker/dockerfile:1@sha256:' + 'c' * 64 + '\n' + GOOD)):
        if problems('sample', text):
            sys.exit(f"Self-test: {label} failed: {problems('sample', text)}")
    for label, text in MUST_FAIL.items():
        if not problems('sample', text):
            sys.exit(f"Self-test: the check passed {label}.")
    return len(MUST_FAIL)


def dockerfiles(root):
    listed = subprocess.run(['git', 'ls-files'], cwd=root, capture_output=True, text=True, check=True).stdout.split('\n')
    return [p for p in listed if p.startswith(FOLDERS) and re.fullmatch(r'(.*/)?Dockerfile(\.[^/.]+)?', p) and not p.endswith('.dockerignore')]


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '.')
    cases = self_test()
    files = dockerfiles(root)
    bad = [] if files else ['no Dockerfile under backend/ or deploy/: this check would hold nothing']
    for path in files:
        bad += problems(path, (root / path).read_text())
    for line in bad:
        print(f"::error::{line}")
    if bad:
        sys.exit(1)
    print(f"Self-test: the good samples passed and all {cases} bad ones failed")
    print(f"{len(files)} production Dockerfile(s): every image and front end pinned by digest")


if __name__ == '__main__':
    main()
