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

Since the 2026-11-28 fix run (H-1, DEC-09) it also holds what may start a laptop job and what the job
holds, so that a later edit cannot quietly widen it:

  - the workflow starts only on `pull_request`, `schedule` or `workflow_dispatch`. A trigger that runs
    a pull request's code with this repository's own rights (`pull_request_target`, `workflow_run`,
    a comment or a label event of another workflow, `workflow_call`) is refused;
  - the job's `if:` is exactly CONDITION: the `HW_RUNNER` switch, and for a pull request both a branch
    of this repository and the owner's `run-on-laptop` label. `workflow_dispatch` and `schedule` need
    write access or come from main, so they are not held to the label;
  - the job's permissions, its own or the workflow's, are written out and only read or none;
  - no expression in the job or the workflow's env names the `secrets` context or `github.token`;
  - every checkout keeps no credentials (`persist-credentials: false`) and checks out this repository;
  - the first step is CLEAN, which empties the work folder, so nothing an earlier job left there is
    read by this one.

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

# H-1 (DEC-09): what may start a laptop job, and what the job holds.
TRIGGERS = {'pull_request', 'schedule', 'workflow_dispatch'}
LABEL = 'run-on-laptop'
CONDITION = ("vars.HW_RUNNER == 'true' && (github.event_name != 'pull_request' || "
             "(github.event.pull_request.head.repo.full_name == github.repository && "
             f"contains(github.event.pull_request.labels.*.name, '{LABEL}')))")
CLEAN = {
    'name': 'Start from an empty work folder',
    'working-directory': '${{ runner.temp }}',
    'run': "\n".join([
        "$workspace = $env:GITHUB_WORKSPACE",
        "if (Test-Path -LiteralPath $workspace) {",
        "  cmd.exe /d /c rmdir /s /q \"$workspace\"",
        "  if (Test-Path -LiteralPath $workspace) { throw \"Could not empty $workspace.\" }",
        "}",
        "New-Item -ItemType Directory -Path $workspace | Out-Null",
    ]),
}
EXPRESSION = re.compile(r'\$\{\{(.*?)\}\}', re.DOTALL)
SECRET = re.compile(r'\bsecrets\b|\bgithub\s*(\.\s*token\b|\[\s*[\'"]token[\'"]\s*\])', re.IGNORECASE)


def is_laptop(job):
    # Hosted only when runs-on is exactly one hosted label; anything else might land on the laptop.
    runs_on = job.get('runs-on')
    return not (isinstance(runs_on, str) and runs_on in HOSTED_LABELS)


def default_shell(node):
    return ((node or {}).get('defaults') or {}).get('run', {}).get('shell')


def triggers(workflow):
    # PyYAML reads the bare key `on` as True.
    on = workflow.get('on', workflow.get(True))
    if isinstance(on, str):
        return {on}
    if isinstance(on, (list, dict)):
        return {str(event) for event in on}
    return {repr(on)}


def expressions(node, conditions=False):
    # Every ${{ }} expression in the node, and every if: value, which is an expression without them.
    found = []
    if isinstance(node, dict):
        for key, value in node.items():
            if key == 'if' and isinstance(value, str):
                found.append(value)
            found += expressions(value)
    elif isinstance(node, list):
        for value in node:
            found += expressions(value)
    elif isinstance(node, str):
        found += EXPRESSION.findall(node)
    return found


def normal(text):
    return ' '.join(str(text).split())


def trust_problems(workflow, name, job, where):
    bad = []
    extra = sorted(triggers(workflow) - TRIGGERS)
    if extra:
        bad.append(f"{where}: laptop job {name!r} is in a workflow that also starts on {extra}; only {sorted(TRIGGERS)} may start a laptop job.")
    if normal(job.get('if', '')) != normal(CONDITION):
        bad.append(f"{where}: laptop job {name!r}: its if: must be exactly {CONDITION!r} (the switch, a branch of this repository and the {LABEL!r} label).")
    permissions = job['permissions'] if 'permissions' in job else workflow.get('permissions')
    if not isinstance(permissions, dict):
        bad.append(f"{where}: laptop job {name!r}: permissions must be written out as a list of read or none, not {permissions!r}.")
    else:
        wide = sorted(scope for scope, level in permissions.items() if level not in ('read', 'none'))
        if wide:
            bad.append(f"{where}: laptop job {name!r} may write {wide}; a laptop job gets read or none.")
    for text in expressions(job) + expressions({'env': workflow.get('env')}):
        if SECRET.search(text):
            bad.append(f"{where}: laptop job {name!r} names a secret or the job token: {normal(text)!r}.")
    steps = job.get('steps') or []
    first = steps[0] if steps else {}
    if not isinstance(first, dict) or set(first) != set(CLEAN) or any(normal(first.get(key)) != normal(value) for key, value in CLEAN.items()):
        bad.append(f"{where}: laptop job {name!r}: its first step must be {CLEAN['name']!r}, exactly as this check holds it.")
    for step in steps:
        if isinstance(step, dict) and str(step.get('uses', '')).startswith('actions/checkout@'):
            options = step.get('with') or {}
            if options.get('persist-credentials') is not False:
                bad.append(f"{where}: laptop job {name!r}: checkout must set persist-credentials: false.")
            kept = sorted(set(options) & {'repository', 'token', 'ssh-key'})
            if kept:
                bad.append(f"{where}: laptop job {name!r}: checkout sets {kept}; it checks out this repository with no credential of its own.")
    return bad


def problems_in(workflow, where):
    bad = []
    workflow_shell = default_shell(workflow)
    for name, job in (workflow.get('jobs') or {}).items():
        if not isinstance(job, dict) or not is_laptop(job):
            continue
        if 'uses' in job:
            bad.append(f"{where}: job {name!r} calls a reusable workflow, whose jobs choose their own runner and which this check cannot follow.")
            continue
        bad += trust_problems(workflow, name, job, where)
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
        'on': {'pull_request': {'paths': ['client/**']}, 'schedule': [{'cron': '17 7 * * *'}], 'workflow_dispatch': None},
        'permissions': {'contents': 'read'},
        'env': {'DOTNET_NOLOGO': '1'},
        'defaults': None,
        'jobs': {
            'hosted': {'runs-on': 'windows-latest', 'steps': [{'uses': 'actions/checkout@v7'}, {'run': 'x', 'shell': 'pwsh'}]},
            'linux': {'runs-on': 'ubuntu-latest', 'steps': [{'uses': 'actions/checkout@v7'}, {'run': 'x'}]},
            'laptop': {
                'if': CONDITION,
                'runs-on': ['self-hosted', 'Windows', 'screentrail-win'],
                'defaults': {'run': {'shell': "powershell -NoProfile -ExecutionPolicy Bypass -Command \". '{0}'\""}},
                'steps': [
                    dict(CLEAN),
                    {'uses': good_sha + '', 'with': {'persist-credentials': False}},
                    {'name': 'e2e', 'run': './run-e2e.ps1', 'env': {'APPDATA': '${{ runner.temp }}/appdata'}},
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
        # H-1: what may start a laptop job (only the three triggers).
        'a pull_request_target trigger': broken(lambda j, w: w['on'].update({'pull_request_target': None})),
        'a workflow_run trigger': broken(lambda j, w: w['on'].update({'workflow_run': {'workflows': ['ci']}})),
        'an issue_comment trigger': broken(lambda j, w: w['on'].update({'issue_comment': None})),
        'a workflow_call trigger': broken(lambda j, w: w['on'].update({'workflow_call': None})),
        'the trigger as a list': broken(lambda j, w: w.update({'on': ['workflow_dispatch', 'pull_request_target']})),
        'the trigger as one word': broken(lambda j, w: w.update({'on': 'push'})),
        'the trigger under the bare key': broken(lambda j, w: w.update({True: w.pop('on') | {'push': None}})),
        # H-1: the job's condition.
        'no condition': broken(lambda j, w: j.pop('if')),
        'no label': broken(lambda j, w: j.update({'if': CONDITION.replace(f" && contains(github.event.pull_request.labels.*.name, '{LABEL}')", '')})),
        'another label': broken(lambda j, w: j.update({'if': CONDITION.replace(LABEL, 'anything')})),
        'the label or the branch': broken(lambda j, w: j.update({'if': CONDITION.replace(' && contains(', ' || contains(')})),
        'no branch check': broken(lambda j, w: j.update({'if': CONDITION.replace('github.event.pull_request.head.repo.full_name == github.repository && ', '')})),
        'no switch': broken(lambda j, w: j.update({'if': CONDITION.replace("vars.HW_RUNNER == 'true' && ", '')})),
        'the condition widened': broken(lambda j, w: j.update({'if': CONDITION + ' || true'})),
        # H-1: no write token, no secret.
        'a write permission on the workflow': broken(lambda j, w: w['permissions'].update({'contents': 'write'})),
        'a write permission on the job': broken(lambda j, w: j.update({'permissions': {'contents': 'read', 'pull-requests': 'write'}})),
        'write-all on the job': broken(lambda j, w: j.update({'permissions': 'write-all'})),
        'read-all as a word': broken(lambda j, w: j.update({'permissions': 'read-all'})),
        'no permissions anywhere': broken(lambda j, w: w.pop('permissions')),
        'a secret in a step': broken(lambda j, w: j['steps'].append({'run': 'x', 'env': {'KEY': '${{ secrets.DRAFTING_KEY }}'}})),
        'a secret by index': broken(lambda j, w: j['steps'].append({'run': 'x', 'env': {'KEY': "${{ secrets['DRAFTING_KEY'] }}"}})),
        'the job token in a step': broken(lambda j, w: j['steps'].append({'run': 'x', 'env': {'GH_TOKEN': '${{ github.token }}'}})),
        'a secret in a step condition': broken(lambda j, w: j['steps'].append({'run': 'x', 'if': "secrets.X != ''"})),
        'a secret in the job env': broken(lambda j, w: j.update({'env': {'KEY': '${{ secrets.KEY }}'}})),
        'a secret in the workflow env': broken(lambda j, w: w['env'].update({'KEY': '${{ secrets.KEY }}'})),
        'a secret in a checkout input': broken(lambda j, w: j['steps'][1]['with'].update({'ssh-key': '${{ secrets.DEPLOY_KEY }}'})),
        # H-1: checkout leaves no credential, of this repository only.
        'checkout keeping its token': broken(lambda j, w: j['steps'][1]['with'].update({'persist-credentials': True})),
        'checkout without the setting': broken(lambda j, w: j['steps'][1].pop('with')),
        'checkout with the setting as text': broken(lambda j, w: j['steps'][1]['with'].update({'persist-credentials': 'false'})),
        'checkout of another repository': broken(lambda j, w: j['steps'][1]['with'].update({'repository': 'someone/else'})),
        # H-1: the work folder is emptied first.
        'no clean step': broken(lambda j, w: j['steps'].pop(0)),
        'the clean step second': broken(lambda j, w: j['steps'].insert(0, j['steps'].pop(1))),
        'the clean step that keeps files': broken(lambda j, w: j['steps'][0].update({'run': CLEAN['run'].replace('rmdir /s /q', 'rmdir /q')})),
        'the clean step that does not check': broken(lambda j, w: j['steps'][0].update({'run': CLEAN['run'].replace('throw', 'Write-Host')})),
        'the clean step inside the work folder': broken(lambda j, w: j['steps'][0].update({'working-directory': '${{ github.workspace }}'})),
        'the clean step with a condition': broken(lambda j, w: j['steps'][0].update({'if': 'false'})),
        'the clean step allowed to fail': broken(lambda j, w: j['steps'][0].update({'continue-on-error': True})),
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
    print("\n".join(bad) if bad else f"{laptop_jobs} laptop job(s) in {len(files)} workflow(s) pin every action, run every step without the profile, start only from the owner's triggers and label, hold no write token or secret, and start from an empty work folder")
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
