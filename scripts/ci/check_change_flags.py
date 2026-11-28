#!/usr/bin/env python3
"""Runs the `changes` job's area step and the `ci-ok` check from ci.yml against made-up changes.

Both are shell scripts inside ci.yml. This takes each one out of the file the way
check_hardware_gate.py takes the gate, runs it here, and holds it to what it has to do for a green
`ci-ok` to mean the area jobs ran:

  - the area step, run in a scratch repository on a made-up change, prints the changed file names
    only inside a stop-commands block, so the runner reads none of them as a workflow command
    whatever the names are. Outside that block it prints no file name and no workflow command but
    its own. The log is read the way the runner reads it: a line that starts with `::` after leading
    spaces, or holds `##[`, is a command, and a stop-commands block ends only at its own token;
  - it refuses a changed path that starts with `::` or holds a line break;
  - an ordinary change still sets every flag, each exactly `true` or `false`, and the right ones;
  - ci-ok passes when every job it needs succeeded or was skipped and every flag the changes job
    declares is exactly `true` or `false`, and fails when a job failed or was cancelled, or any one
    of those flags is empty, missing or anything else. Each flag is tried in turn, so a flag added
    to the changes job and not to ci-ok fails here.

Usage: check_change_flags.py [repo root] [path to ci.yml, default .github/workflows/ci.yml]
"""

import json
import os
import pathlib
import subprocess
import sys
import tempfile

import yaml

OWN_COMMANDS = {'error', 'warning', 'notice'}


def load(ci_path):
    workflow = yaml.safe_load(open(ci_path))
    jobs = workflow.get('jobs') or {}
    changes, ok = jobs.get('changes'), jobs.get('ci-ok')
    if not changes or not ok:
        sys.exit(f"{ci_path} has no changes job or no ci-ok job.")
    areas = [s for s in changes.get('steps') or [] if s.get('id') == 'areas']
    if len(areas) != 1 or 'run' not in areas[0]:
        sys.exit(f"{ci_path}: the changes job has no single step with id 'areas'.")
    flags = sorted((changes.get('outputs') or {}).keys())
    if not flags:
        sys.exit(f"{ci_path}: the changes job declares no outputs.")
    checks = [s for s in ok.get('steps') or [] if 'RESULTS' in (s.get('env') or {})]
    if len(checks) != 1 or 'run' not in checks[0]:
        sys.exit(f"{ci_path}: ci-ok has no single step that reads RESULTS.")
    needs = ok.get('needs') or []
    needs = [needs] if isinstance(needs, str) else list(needs)
    return areas[0]['run'], flags, checks[0]['run'], needs


def command(line):
    """The workflow command a log line is, as the runner reads it: (name, data), or None."""
    text = line.lstrip()
    if text.startswith('::'):
        end = text.find('::', 2)
        if end >= 0:
            return text[2:end].split(' ', 1)[0], text[end + 2:]
    start = line.find('##[')
    if start >= 0:
        end = line.find(']', start)
        if end >= 0:
            return line[start + 3:end].split(' ', 1)[0], line[end + 1:]
    return None


def read_log(log):
    """Split a step's log into the lines the runner reads as commands and the lines it does not."""
    outside, inside, token, closed = [], [], None, True
    for line in log.splitlines():
        found = command(line)
        if token is not None:
            if found and found[0].lower() == token.lower():
                token, closed = None, True
            else:
                inside.append(line)
            continue
        if found and found[0] == 'stop-commands' and found[1]:
            token, closed = found[1], False
            continue
        outside.append(line)
    return outside, inside, closed


def git(repo, *args, env):
    subprocess.run(['git', *args], cwd=repo, env=env, check=True, capture_output=True)


def area_step(script, work, names, env):
    """Commit `names` on top of a first commit in a scratch repository and run the area step there."""
    repo = work / 'repo'
    repo.mkdir()
    git(repo, 'init', '-q', '-b', 'main', env=env)
    (repo / 'README.md').write_text('scratch\n')
    git(repo, 'add', '-A', env=env)
    git(repo, 'commit', '-q', '-m', 'first', env=env)
    before = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=repo, env=env, check=True,
                            capture_output=True, text=True).stdout.strip()
    for name in names:
        path = repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('x\n')
    git(repo, 'add', '-A', env=env)
    git(repo, 'commit', '-q', '-m', 'change', env=env)
    output = work / 'output'
    output.write_text('')
    step_env = dict(env, EVENT='push', PUSH_BEFORE=before, PR_BASE='', PR_HEAD='', GITHUB_OUTPUT=str(output))
    done = subprocess.run(['bash', '-c', script], cwd=repo, env=step_env, stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT, text=True, timeout=60)
    return done.returncode, done.stdout, output.read_text()


ORDINARY = {'client': 'true', 'installer': 'true'}

# (name, changed files, refused, flags that must be true; the rest must be false)
AREA_CASES = [
    ('a name the runner would read as a command', ['::add-mask::true', 'client/App.cs'], True, None),
    ('a name the runner would read as an old-style command', ['##[add-mask]true', 'client/App.cs'], False, ORDINARY),
    ('a name with a line break', ['docs/a\n::add-mask::true', 'client/App.cs'], True, None),
    ('an ordinary change', ['client/App.cs', 'docs/a note with spaces.md', 'docs/naïve.md'], False, ORDINARY),
    ('a change outside every area', ['docs/notes.md'], False, {}),
]


def check_areas(script, flags, env):
    bad = []
    for name, files, refused, true_flags in AREA_CASES:
        problems = []
        with tempfile.TemporaryDirectory() as temp:
            work = pathlib.Path(temp)
            code, log, output = area_step(script, work, files, env)
        outside, inside, closed = read_log(log)
        if not closed:
            problems.append("a stop-commands block is never closed")
        for line in outside:
            found = command(line)
            if found and found[0] not in OWN_COMMANDS:
                problems.append(f"the runner would read {line!r} as a workflow command")
            if any(part and part in line for file in files for part in file.split('\n')
                   if part not in ('client', 'docs')):
                problems.append(f"a changed file name is printed outside the stop-commands block: {line!r}")
        if refused:
            if code == 0:
                problems.append("the step did not refuse the changed path")
        else:
            if code != 0:
                problems.append(f"the step exited {code}")
            if not any('client/App.cs' in line for line in inside) and 'client/App.cs' in files:
                problems.append("the changed files are not listed")
            got = dict(line.split('=', 1) for line in output.splitlines() if '=' in line)
            want = {flag: 'false' for flag in flags}
            want.update(true_flags)
            if got != want:
                problems.append(f"flags {got}, expected {want}")
        if problems:
            tail = ' | '.join(line for line in log.splitlines()[-3:] if line.strip())
            bad.append(f"areas, {name}: {'; '.join(problems)}. Step said: {tail}")
    return bad


def ci_ok(script, results, env):
    done = subprocess.run(['bash', '-c', script], env=dict(env, RESULTS=json.dumps(results)),
                          capture_output=True, text=True, timeout=60)
    return done.returncode


def check_ci_ok(script, flags, needs, env):
    if 'changes' not in needs:
        return ["ci-ok does not need the changes job"]

    good = {flag: 'true' if index % 2 else 'false' for index, flag in enumerate(flags)}

    def results(result='success', outputs=None, failed=None):
        made = {job: {'result': result, 'outputs': {}} for job in needs}
        made['changes'] = {'result': 'success', 'outputs': good if outputs is None else outputs}
        if failed:
            made[failed[0]]['result'] = failed[1]
        return made

    other = next(job for job in needs if job != 'changes')
    cases = [
        ('every job passed', results(), 0),
        ('the area jobs skipped', results('skipped'), 0),
        (f'{other} failed', results(failed=(other, 'failure')), 1),
        (f'{other} cancelled', results(failed=(other, 'cancelled')), 1),
        ('the changes job reported no flags', results(outputs={}), 1),
    ]
    for flag in flags:
        cases.append((f"the '{flag}' flag empty", results('skipped', outputs=dict(good, **{flag: ''})), 1))
        cases.append((f"the '{flag}' flag missing", results('skipped', outputs={k: v for k, v in good.items() if k != flag}), 1))
    cases.append(("a flag that is 'TRUE'", results('skipped', outputs=dict(good, **{flags[0]: 'TRUE'})), 1))

    bad = []
    for name, made, want in cases:
        got = ci_ok(script, made, env)
        if (got == 0) != (want == 0):
            bad.append(f"ci-ok, {name}: exit {got}, expected {'a pass' if want == 0 else 'a failure'}")
    return bad


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '.')
    ci_path = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else root / '.github/workflows/ci.yml'
    areas, flags, ok, needs = load(ci_path)

    with tempfile.TemporaryDirectory() as home:
        # No configuration from this machine reaches the scratch repositories.
        env = {
            'PATH': os.environ['PATH'],
            'HOME': home,
            'GIT_CONFIG_GLOBAL': os.devnull,
            'GIT_CONFIG_NOSYSTEM': '1',
            'GIT_AUTHOR_NAME': 'ci', 'GIT_AUTHOR_EMAIL': 'ci@example.invalid',
            'GIT_COMMITTER_NAME': 'ci', 'GIT_COMMITTER_EMAIL': 'ci@example.invalid',
            'LC_ALL': 'C',
        }
        bad = check_areas(areas, flags, env) + check_ci_ok(ok, flags, needs, env)

    for line in bad:
        print(f"::error file=.github/workflows/ci.yml::{line}")
    print("\n".join(bad) if bad else
          f"the area step answered all {len(AREA_CASES)} changes without a file name read as a command, "
          f"and ci-ok passes only with every one of the {len(flags)} flags true or false")
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
