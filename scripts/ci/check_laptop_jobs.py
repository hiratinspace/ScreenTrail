#!/usr/bin/env python3
"""Holds the shape of every workflow job that can run on the spare laptop (2026-11-15 review).

The laptop's self-hosted runner signs in as the account that also runs the demo, so whatever a job
there executes can read what that account holds. Two rules keep what a job runs to what this
repository says:

  - every `uses:` is pinned to a full 40-character commit, so a moved tag cannot run on the laptop.
    A local action (`./...`) and a job that calls a reusable workflow are reported, because their own
    steps choose actions and shells this check does not read;
  - every `run:` step runs in Windows PowerShell with `-NoProfile`, whether its shell comes from the
    step, the job's defaults or the workflow's defaults, so the account's profile script never runs
    before a step.

A job is hosted only when its runs-on is exactly one hosted label (HOSTED_LABELS). Every other job,
whatever its runs-on says or however it is written (other labels, a list, a mapping, a runner group, an
expression anywhere, or no runs-on at all), could reach the laptop and is held to these rules. Both
`.yml` and `.yaml` workflow files are read. The checks are tried first on made-up jobs that must pass
and must fail, so a check that passes everything fails.

Usage: check_laptop_jobs.py [repo root] [workflow file ...]
"""

import copy
import pathlib
import re
import sys
import tempfile

import yaml

HOSTED_LABELS = {'ubuntu-latest', 'windows-latest'}
PINNED = re.compile(r'^[^@\s]+@[0-9a-f]{40}$')
SHELL = re.compile(r'^powershell(\.exe)?\s+-NoProfile(\s|$)', re.IGNORECASE)


def is_laptop(job):
    # Hosted only when runs-on is exactly one hosted label; anything else might land on the laptop.
    runs_on = job.get('runs-on')
    return not (isinstance(runs_on, str) and runs_on in HOSTED_LABELS)


def default_shell(node):
    return ((node or {}).get('defaults') or {}).get('run', {}).get('shell')


def problems_in(workflow, where):
    bad = []
    workflow_shell = default_shell(workflow)
    for name, job in (workflow.get('jobs') or {}).items():
        if not isinstance(job, dict) or not is_laptop(job):
            continue
        if 'uses' in job:
            bad.append(f"{where}: job {name!r} calls a reusable workflow, whose jobs choose their own runner and which this check cannot follow.")
            continue
        job_shell = default_shell(job) or workflow_shell
        for shell, level in ((workflow_shell, 'the workflow defaults'), (default_shell(job), 'its defaults')):
            if shell is not None and not SHELL.match(str(shell)):
                bad.append(f"{where}: laptop job {name!r}: {level} set shell {shell!r}, which does not start with 'powershell -NoProfile'.")
        for index, step in enumerate(job.get('steps') or []):
            label = step.get('name') or step.get('uses') or f'step {index + 1}'
            uses = step.get('uses')
            if uses is not None and str(uses).startswith('./'):
                bad.append(f"{where}: laptop job {name!r}, {label!r}: {uses!r} is a local action, whose own steps this check does not read.")
            elif uses is not None and not PINNED.match(str(uses)):
                bad.append(f"{where}: laptop job {name!r}, {label!r}: {uses!r} is not pinned to a 40-character commit.")
            if 'run' in step:
                shell = step.get('shell') or job_shell
                if shell is None:
                    bad.append(f"{where}: laptop job {name!r}, {label!r}: no shell is set, so the runner's default (with the profile) runs it.")
                elif not SHELL.match(str(shell)):
                    bad.append(f"{where}: laptop job {name!r}, {label!r}: shell {shell!r} does not start with 'powershell -NoProfile'.")
    return bad


def self_test():
    good_sha = 'actions/checkout@' + 'a' * 40
    good = {
        'defaults': None,
        'jobs': {
            'hosted': {'runs-on': 'windows-latest', 'steps': [{'uses': 'actions/checkout@v7'}, {'run': 'x', 'shell': 'pwsh'}]},
            'linux': {'runs-on': 'ubuntu-latest', 'steps': [{'uses': 'actions/checkout@v7'}, {'run': 'x'}]},
            'laptop': {
                'runs-on': ['self-hosted', 'Windows', 'screentrail-win'],
                'defaults': {'run': {'shell': "powershell -NoProfile -ExecutionPolicy Bypass -Command \". '{0}'\""}},
                'steps': [
                    {'uses': good_sha + ''},
                    {'run': 'x'},
                    {'run': 'y', 'shell': 'powershell -NoProfile -Command ". \'{0}\'"'},
                ],
            },
        },
    }
    if problems_in(good, 'sample'):
        sys.exit(f"Self-test: a correct sample failed: {problems_in(good, 'sample')}")

    def broken(change):
        sample = copy.deepcopy(good)
        change(sample['jobs']['laptop'], sample)
        return sample

    must_fail = {
        'an action by tag': broken(lambda j, w: j['steps'].append({'uses': 'actions/upload-artifact@v7'})),
        'an action by a short commit': broken(lambda j, w: j['steps'].append({'uses': 'actions/checkout@3d3c42e'})),
        'a job default with the profile': broken(lambda j, w: j['defaults']['run'].update(shell="powershell -ExecutionPolicy Bypass -Command \". '{0}'\"")),
        'a step with the profile': broken(lambda j, w: j['steps'].append({'run': 'x', 'shell': 'powershell'})),
        'a step in pwsh': broken(lambda j, w: j['steps'].append({'run': 'x', 'shell': 'pwsh -NoProfile'})),
        'no shell anywhere': broken(lambda j, w: j.pop('defaults')),
        'the label alone': broken(lambda j, w: j.update({'runs-on': 'screentrail-win', 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'runs-on from an expression': broken(lambda j, w: j.update({'runs-on': '${{ inputs.where }}', 'steps': [{'uses': 'actions/checkout@v7'}]})),
        # Any runs-on that is not one hosted label is held to the laptop rules, however it is written.
        'the runner\'s default labels without self-hosted': broken(lambda j, w: j.update({'runs-on': ['Windows', 'X64'], 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'an unknown label': broken(lambda j, w: j.update({'runs-on': 'some-runner', 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'a hosted label beside another': broken(lambda j, w: j.update({'runs-on': ['windows-latest', 'X64'], 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'an expression inside a list': broken(lambda j, w: j.update({'runs-on': ['${{ vars.WHERE }}'], 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'an expression in the mapping form': broken(lambda j, w: j.update({'runs-on': {'labels': '${{ vars.WHERE }}'}, 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'a runner group': broken(lambda j, w: j.update({'runs-on': {'group': 'anything'}, 'steps': [{'uses': 'actions/checkout@v7'}]})),
        'a job that calls a reusable workflow': broken(lambda j, w: w['jobs'].update({'caller': {'uses': 'owner/repo/.github/workflows/w.yml@main'}})),
        'a local action': broken(lambda j, w: j['steps'].append({'uses': './.github/actions/local'})),
    }
    for case, sample in must_fail.items():
        if not problems_in(sample, 'sample'):
            sys.exit(f"Self-test: {case} passed, and it must fail.")

    loader_test()


def workflow_files(root):
    folder = root / '.github/workflows'
    return sorted(list(folder.glob('*.yml')) + list(folder.glob('*.yaml')))


def loader_test():
    # The file list itself, on a made-up repository: GitHub runs a .yaml workflow as readily as a .yml one.
    with tempfile.TemporaryDirectory() as temporary:
        root = pathlib.Path(temporary)
        (root / '.github/workflows').mkdir(parents=True)
        (root / '.github/workflows/ci.yml').write_text('jobs: {}\n')
        (root / '.github/workflows/other.yaml').write_text('jobs: {}\n')
        if root / '.github/workflows/other.yaml' not in workflow_files(root):
            sys.exit("Self-test: a .yaml workflow is not read, and GitHub runs it.")


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '.')
    files = [pathlib.Path(f) for f in sys.argv[2:]] or workflow_files(root)
    self_test()

    bad, laptop_jobs = [], 0
    for path in files:
        workflow = yaml.safe_load(path.read_text())
        laptop_jobs += sum(1 for job in (workflow.get('jobs') or {}).values() if isinstance(job, dict) and is_laptop(job))
        bad += problems_in(workflow, path.name)

    for line in bad:
        print(f"::error::{line}")
    print("\n".join(bad) if bad else f"{laptop_jobs} laptop job(s) in {len(files)} workflow(s) pin every action and run every step without the profile")
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
