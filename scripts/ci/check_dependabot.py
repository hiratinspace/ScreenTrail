#!/usr/bin/env python3
"""Holds what .github/dependabot.yml watches (H-2).

Every manifest a shipped part of the product is built from is watched, so a fixed release of one of
its dependencies is proposed rather than missed:

  - npm: the site, the site's deploy tooling, the web admin and the shared schema;
  - nuget: the client and the backend (their Directory.Packages.props);
  - docker: the backend's and the web proxy's Dockerfiles; docker-compose: deploy/compose.yaml.

And one thing is never watched: GitHub Actions. A Dependabot branch lives in this repository, so an
update to a workflow would be a branch of this repository changing what runs on the laptop runner.
Actions are pinned by commit and moved by hand (scripts/ci/check_laptop_jobs.py).

Each watched folder must exist, so a renamed folder fails here instead of going quiet. The checks are
tried first on made-up files that must pass and must fail.

Usage: check_dependabot.py [repo root]
"""

import pathlib
import sys

import yaml

WATCHED = {
    ('npm', '/site'),
    ('npm', '/site/deploy'),
    ('npm', '/web'),
    ('npm', '/shared/schema'),
    ('nuget', '/client'),
    ('nuget', '/backend'),
    ('docker', '/backend'),
    ('docker', '/deploy/web'),
    ('docker-compose', '/deploy'),
}
NEVER = {'github-actions'}


def entries(config):
    found = []
    for update in (config or {}).get('updates') or []:
        if not isinstance(update, dict):
            continue
        folders = update.get('directories') or [update.get('directory')]
        for folder in folders:
            found.append((str(update.get('package-ecosystem')), str(folder).rstrip('/') or '/'))
    return found


def problems(config, exists=lambda folder: True):
    bad = []
    if not isinstance(config, dict) or config.get('version') != 2:
        return ['dependabot.yml must be version 2 with a list of updates']
    found = entries(config)
    for ecosystem, folder in sorted(WATCHED - set(found)):
        bad.append(f"dependabot.yml does not watch {ecosystem} in {folder}")
    for ecosystem, folder in found:
        if ecosystem in NEVER:
            bad.append(f"dependabot.yml watches {ecosystem} in {folder}: a Dependabot branch would change what runs on the laptop runner")
        elif not exists(folder):
            bad.append(f"dependabot.yml watches {ecosystem} in {folder}, which does not exist")
    for update in config.get('updates') or []:
        if not isinstance(update, dict) or not (update.get('schedule') or {}).get('interval'):
            bad.append(f"dependabot.yml has an update with no schedule: {update!r}")
    return bad


def sample():
    return {'version': 2, 'updates': [
        {'package-ecosystem': ecosystem, 'directory': folder, 'schedule': {'interval': 'weekly'}}
        for ecosystem, folder in sorted(WATCHED)
    ]}


def self_test():
    if problems(sample()):
        sys.exit(f"Self-test: the made-up good file failed: {problems(sample())}")
    must_fail = {}
    for index, (ecosystem, folder) in enumerate(sorted(WATCHED)):
        config = sample()
        config['updates'].pop(index)
        must_fail[f'no {ecosystem} in {folder}'] = config
    config = sample()
    config['updates'].append({'package-ecosystem': 'github-actions', 'directory': '/', 'schedule': {'interval': 'weekly'}})
    must_fail['github-actions'] = config
    config = sample()
    config['updates'].append({'package-ecosystem': 'github-actions', 'directories': ['/', '/.github/actions/*'], 'schedule': {'interval': 'daily'}})
    must_fail['github-actions in a list of folders'] = config
    config = sample()
    config['updates'][0]['directory'] = '/elsewhere'
    must_fail['a watched folder moved'] = config
    config = sample()
    config['updates'][0].pop('schedule')
    must_fail['an update with no schedule'] = config
    must_fail['version 1'] = dict(sample(), version=1)
    for label, config in must_fail.items():
        if not problems(config):
            sys.exit(f"Self-test: the check passed a file with {label}.")
    config = sample()
    config['updates'][0]['directory'] = '/nowhere'
    if not problems(config, exists=lambda folder: folder != '/nowhere'):
        sys.exit("Self-test: the check passed a folder that does not exist.")
    return len(must_fail) + 1


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '.')
    cases = self_test()
    path = root / '.github/dependabot.yml'
    config = yaml.safe_load(path.read_text()) if path.exists() else None
    bad = problems(config, exists=lambda folder: (root / folder.lstrip('/')).is_dir())
    for line in bad:
        print(f"::error file=.github/dependabot.yml::{line}")
    if bad:
        sys.exit(1)
    print(f"Self-test: the good file passed and all {cases} bad ones failed")
    print(f"Dependabot watches all {len(WATCHED)} manifests and no workflow")


if __name__ == '__main__':
    main()
