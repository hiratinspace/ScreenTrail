#!/usr/bin/env python3
"""Holds what a job with a deployment environment may run, and the shape of the site deploy (G-04).

A job that names an environment can be given that environment's secrets, and in the site deploy it
is: the Cloudflare token. A dependency's install script, its tests or its build tools run as code in
whichever job runs them, so none of them may run in such a job. For every job in every workflow that
names an environment:

  - an npm install (`npm ci`, `npm install` and the like) carries `--ignore-scripts`;
  - no `npm test`, `npm run`, `npm start`, `npm exec`, `npx`, yarn, pnpm, bun or corepack;
  - no setup-node cache, which another workflow can write.

And for site-deploy.yml itself:

  - two jobs. `build` names no environment and no secret, installs, tests and builds the site, and
    uploads `site/dist` as the artifact `site-dist`. `deploy` needs it, runs in `site-production`,
    installs Wrangler alone from `site/deploy` with `npm ci --ignore-scripts`, downloads the artifact
    and runs exactly that Wrangler;
  - every action is pinned to a commit;
  - `site/deploy/package.json` holds Wrangler and nothing else, at the version `site/package.json`
    pins, and no scripts; every package in `site/deploy/package-lock.json` is in
    `site/package-lock.json` at the same version, address and integrity, so the deploy job installs
    nothing the reviewed site lockfile does not already hold.

The pilot form's addresses (G-07) reach the deploy job as environment secrets, `SITE_PILOT_FROM`
and `SITE_PILOT_TO`, which the runner masks in the log; Wrangler prints the mail binding's addresses
when it deploys. No workflow reads either from `vars`, which GitHub prints as they are.

The checks are tried first on made-up samples that must pass and must fail.

Usage: check_site_deploy.py [repo root]
"""

import copy
import json
import pathlib
import re
import sys

import yaml

PINNED = re.compile(r'^[^@\s]+@[0-9a-f]{40}$')
INSTALL = re.compile(r'\bnpm\s+(ci|install|i|in|isntall|add|update|up|upgrade|rebuild|rb|link|ln)\b(?P<rest>[^\n;&|]*)')
RUNS_CODE = re.compile(r'\bnpm\s+(test|t|tst|run|run-script|rum|urn|start|restart|stop|exec|x|explore)\b|\bnpx\b|\b(yarn|pnpm|pnpx|bun|bunx|corepack)\b')
EXPRESSION = re.compile(r'\$\{\{(.*?)\}\}', re.DOTALL)
SECRET = re.compile(r'\bsecrets\b|\bgithub\s*(\.\s*token\b|\[\s*[\'"]token[\'"]\s*\])', re.IGNORECASE)
WRANGLER = './deploy/node_modules/.bin/wrangler deploy --config wrangler.deploy.json'
ARTIFACT = 'site-dist'
PILOT = {'PILOT_FROM': '${{ secrets.SITE_PILOT_FROM }}', 'PILOT_TO': '${{ secrets.SITE_PILOT_TO }}'}
PILOT_VARIABLE = re.compile(r'\bvars\s*(\.\s*|\[\s*[\'"])SITE_PILOT_(FROM|TO)\b', re.IGNORECASE)
CONFIG_STEP = 'Write the deploy configuration (the real domain and the pilot form\'s addresses)'


def texts(node):
    found = []
    if isinstance(node, dict):
        for value in node.values():
            found += texts(value)
    elif isinstance(node, list):
        for value in node:
            found += texts(value)
    elif isinstance(node, str):
        found.append(node)
    return found


def names_secret(node):
    found = []
    for text in texts(node):
        found += [e for e in EXPRESSION.findall(text) if SECRET.search(e)]
    if isinstance(node, dict) and isinstance(node.get('if'), str) and SECRET.search(node['if']):
        found.append(node['if'])
    return found


def environment_problems(workflow, where):
    bad = []
    for name, job in (workflow.get('jobs') or {}).items():
        if not isinstance(job, dict) or 'environment' not in job:
            continue
        for index, step in enumerate(job.get('steps') or []):
            label = step.get('name') or step.get('uses') or f'step {index + 1}'
            run = str(step.get('run') or '')
            for match in INSTALL.finditer(run):
                if '--ignore-scripts' not in match.group('rest').split():
                    bad.append(f"{where}: job {name!r} names an environment, and {label!r} runs {match.group(0).strip()!r} without --ignore-scripts.")
            for match in RUNS_CODE.finditer(run):
                bad.append(f"{where}: job {name!r} names an environment, and {label!r} runs {match.group(0)!r}, which runs a package's code.")
            uses = str(step.get('uses') or '')
            if uses.startswith('actions/setup-node@') and (step.get('with') or {}).get('cache'):
                bad.append(f"{where}: job {name!r} names an environment, and {label!r} reads a package cache.")
    return bad


def step_named(job, name):
    found = [s for s in (job.get('steps') or []) if isinstance(s, dict) and s.get('name') == name]
    return found[0] if len(found) == 1 else None


def site_problems(workflow, files):
    where = 'site-deploy.yml'
    jobs = workflow.get('jobs') or {}
    if set(jobs) != {'build', 'deploy'}:
        return [f"{where}: must have exactly the jobs build and deploy, not {sorted(jobs)}."]
    build, deploy = jobs['build'], jobs['deploy']
    bad = []
    for name, job in jobs.items():
        for index, step in enumerate(job.get('steps') or []):
            uses = step.get('uses')
            if uses is not None and not PINNED.match(str(uses)):
                bad.append(f"{where}: job {name!r} uses {uses!r}, not pinned to a 40-character commit.")
    if 'environment' in build:
        bad.append(f"{where}: the build job names an environment; it runs the packages' code and gets no secret.")
    if names_secret(build) or names_secret({'env': workflow.get('env')}):
        bad.append(f"{where}: the build job or the workflow env names a secret or the job token.")
    upload = [s for s in build.get('steps') or [] if str(s.get('uses', '')).startswith('actions/upload-artifact@')]
    if len(upload) != 1 or (upload[0].get('with') or {}).get('name') != ARTIFACT or (upload[0].get('with') or {}).get('path') != 'site/dist':
        bad.append(f"{where}: the build job must upload site/dist, and only that, as {ARTIFACT!r}.")
    environment = deploy.get('environment')
    if (environment.get('name') if isinstance(environment, dict) else environment) != 'site-production':
        bad.append(f"{where}: the deploy job must run in the site-production environment.")
    if deploy.get('needs') not in ('build', ['build']):
        bad.append(f"{where}: the deploy job must need the build job.")
    runs = [str(s.get('run') or '').strip() for s in deploy.get('steps') or [] if 'run' in s]
    installs = [s for s in deploy.get('steps') or [] if INSTALL.search(str(s.get('run') or ''))]
    if len(installs) != 1 or str(installs[0].get('run')).strip() != 'npm ci --ignore-scripts' or installs[0].get('working-directory') != 'site/deploy':
        bad.append(f"{where}: the deploy job installs once, `npm ci --ignore-scripts` in site/deploy.")
    if WRANGLER not in runs:
        bad.append(f"{where}: the deploy job must run exactly {WRANGLER!r}.")
    if any(re.search(r'\bwrangler\b(?!\.)', r) and r != WRANGLER for r in runs):
        bad.append(f"{where}: the deploy job runs Wrangler some other way than {WRANGLER!r}.")
    download = [s for s in deploy.get('steps') or [] if str(s.get('uses', '')).startswith('actions/download-artifact@')]
    if len(download) != 1 or (download[0].get('with') or {}).get('name') != ARTIFACT or (download[0].get('with') or {}).get('path') != 'site/dist':
        bad.append(f"{where}: the deploy job must download {ARTIFACT!r} into site/dist, once.")
    config = step_named(deploy, CONFIG_STEP)
    if config is None or any((config.get('env') or {}).get(key) != value for key, value in PILOT.items()):
        bad.append(f"{where}: the step {CONFIG_STEP!r} must take the pilot addresses from the environment secrets {sorted(PILOT.values())}, which the log masks.")
    bad += deploy_package_problems(files)
    return bad


def deploy_package_problems(files):
    bad = []
    try:
        site = json.loads(files['site/package.json'])
        own = json.loads(files['site/deploy/package.json'])
        site_lock = json.loads(files['site/package-lock.json'])
        lock = json.loads(files['site/deploy/package-lock.json'])
    except (KeyError, ValueError) as error:
        return [f"site/deploy: a package file is missing or not JSON ({error})."]
    version = (site.get('devDependencies') or {}).get('wrangler') or (site.get('dependencies') or {}).get('wrangler')
    if own.get('dependencies') != {'wrangler': version} or own.get('devDependencies') or own.get('optionalDependencies') or own.get('peerDependencies') or own.get('scripts') or own.get('overrides'):
        bad.append(f"site/deploy/package.json must depend on wrangler {version} alone (as site/package.json pins it), with no scripts.")
    packages = lock.get('packages') or {}
    if (packages.get('') or {}).get('dependencies') != own.get('dependencies'):
        bad.append("site/deploy/package-lock.json does not match site/deploy/package.json.")
    reviewed = site_lock.get('packages') or {}
    for path, entry in packages.items():
        if not path:
            continue
        known = reviewed.get(path)
        if not known or any(known.get(key) != entry.get(key) for key in ('version', 'resolved', 'integrity')):
            bad.append(f"site/deploy/package-lock.json holds {path} {entry.get('version')}, which site/package-lock.json does not hold at that version, address and integrity.")
    if 'node_modules/wrangler' not in packages:
        bad.append("site/deploy/package-lock.json does not hold wrangler.")
    return bad


def check(workflows, files):
    bad = []
    for where, workflow in workflows.items():
        bad += environment_problems(workflow, where)
        for text in texts(workflow):
            if PILOT_VARIABLE.search(text):
                bad.append(f"{where}: reads a pilot address from a variable, which the log prints as it is: {text.strip()!r}.")
    if 'site-deploy.yml' in workflows:
        bad += site_problems(workflows['site-deploy.yml'], files)
    else:
        bad.append('site-deploy.yml is missing.')
    return bad


def sample():
    pin = 'a' * 40
    deploy_lock = {'name': 'd', 'lockfileVersion': 3, 'packages': {
        '': {'name': 'd', 'dependencies': {'wrangler': '4.0.0'}},
        'node_modules/wrangler': {'version': '4.0.0', 'resolved': 'https://registry.npmjs.org/wrangler/-/wrangler-4.0.0.tgz', 'integrity': 'sha512-w'},
        'node_modules/esbuild': {'version': '0.1.0', 'resolved': 'https://registry.npmjs.org/esbuild/-/esbuild-0.1.0.tgz', 'integrity': 'sha512-e'},
    }}
    site_lock = copy.deepcopy(deploy_lock)
    site_lock['packages']['node_modules/astro'] = {'version': '7.0.0', 'resolved': 'https://registry.npmjs.org/astro/-/astro-7.0.0.tgz', 'integrity': 'sha512-a'}
    files = {
        'site/package.json': json.dumps({'devDependencies': {'wrangler': '4.0.0', 'astro': '7.0.0'}, 'scripts': {'build': 'astro build'}}),
        'site/deploy/package.json': json.dumps({'name': 'd', 'private': True, 'dependencies': {'wrangler': '4.0.0'}}),
        'site/package-lock.json': json.dumps(site_lock),
        'site/deploy/package-lock.json': json.dumps(deploy_lock),
    }
    workflow = {
        'on': {'push': {'branches': ['main']}, 'workflow_dispatch': None},
        'permissions': {'contents': 'read'},
        'jobs': {
            'build': {
                'runs-on': 'ubuntu-latest',
                'env': {'SITE_URL': '${{ vars.SITE_URL }}'},
                'steps': [
                    {'uses': 'actions/checkout@' + pin, 'with': {'persist-credentials': False}},
                    {'uses': 'actions/setup-node@' + pin, 'with': {'node-version-file': '.nvmrc'}},
                    {'run': 'npm ci'},
                    {'run': 'npm test'},
                    {'run': 'npm run build'},
                    {'uses': 'actions/upload-artifact@' + pin, 'with': {'name': ARTIFACT, 'path': 'site/dist', 'if-no-files-found': 'error'}},
                ],
            },
            'deploy': {
                'needs': 'build',
                'runs-on': 'ubuntu-latest',
                'environment': {'name': 'site-production', 'url': '${{ vars.SITE_URL }}'},
                'steps': [
                    {'uses': 'actions/checkout@' + pin, 'with': {'persist-credentials': False}},
                    {'uses': 'actions/setup-node@' + pin, 'with': {'node-version-file': '.nvmrc'}},
                    {'name': 'Install Wrangler', 'working-directory': 'site/deploy', 'run': 'npm ci --ignore-scripts'},
                    {'uses': 'actions/download-artifact@' + pin, 'with': {'name': ARTIFACT, 'path': 'site/dist'}},
                    {'name': CONFIG_STEP, 'env': dict(PILOT, SITE_URL='${{ vars.SITE_URL }}'), 'run': 'node scripts/deploy-config.mjs > wrangler.deploy.json'},
                    {'name': 'Deploy', 'env': {'CLOUDFLARE_API_TOKEN': '${{ secrets.CLOUDFLARE_API_TOKEN }}'}, 'run': WRANGLER},
                ],
            },
        },
    }
    other = {'jobs': {'sign': {'runs-on': 'windows-latest', 'environment': 'signing', 'steps': [{'run': 'pwsh installer/sign.ps1'}]},
                      'site': {'runs-on': 'ubuntu-latest', 'steps': [{'run': 'npm ci'}, {'run': 'npx playwright test'}]}}}
    return {'site-deploy.yml': workflow, 'ci.yml': other}, files


def edit(where, change):
    def apply(workflows, files):
        change(workflows[where] if where.endswith('.yml') else files, files)
    return apply


def site_step(job, index, **fields):
    return lambda w, f: w['jobs'][job]['steps'][index].update(fields)


def edit_json(path, change):
    def apply(workflows, files):
        data = json.loads(files[path])
        change(data)
        files[path] = json.dumps(data)
    return apply


MUST_FAIL = {
    # One job again: the packages' code and the token in the same job.
    'one job that installs, builds and deploys': edit('site-deploy.yml', lambda w, f: w['jobs'].update({'build': {**w['jobs']['build'], 'environment': 'site-production'}})),
    'npm ci in the deploy job': edit('site-deploy.yml', site_step('deploy', 2, run='npm ci')),
    'npm install in the deploy job': edit('site-deploy.yml', site_step('deploy', 2, run='npm install wrangler')),
    'the flag on another command': edit('site-deploy.yml', site_step('deploy', 2, run='npm ci && echo --ignore-scripts')),
    'npm test in the deploy job': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].insert(3, {'run': 'npm test'})),
    'npm run build in the deploy job': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].insert(3, {'run': 'npm run build'})),
    'npx wrangler in the deploy job': edit('site-deploy.yml', site_step('deploy', 5, run='npx wrangler deploy --config wrangler.deploy.json')),
    'wrangler from the site folder': edit('site-deploy.yml', site_step('deploy', 5, run='./node_modules/.bin/wrangler deploy --config wrangler.deploy.json')),
    'a second wrangler call': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].append({'run': 'node_modules/.bin/wrangler secret put X'})),
    'yarn in the deploy job': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].insert(3, {'run': 'yarn install --ignore-scripts'})),
    'pnpm in the deploy job': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].insert(3, {'run': 'pnpm i'})),
    'a setup-node cache in the deploy job': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][1]['with'].update({'cache': 'npm'})),
    'the install in the site folder': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][2].pop('working-directory')),
    'no artifact download': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'].pop(3)),
    'another artifact': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][3]['with'].update({'name': 'other'})),
    'deploy without needing build': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy'].pop('needs')),
    'deploy in another environment': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy'].update({'environment': 'production'})),
    'deploy with no environment': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy'].pop('environment')),
    'a secret in the build job': edit('site-deploy.yml', lambda w, f: w['jobs']['build']['env'].update({'T': '${{ secrets.CLOUDFLARE_API_TOKEN }}'})),
    'a secret in the workflow env': edit('site-deploy.yml', lambda w, f: w.update({'env': {'T': '${{ secrets.CLOUDFLARE_API_TOKEN }}'}})),
    'the build uploads the whole site': edit('site-deploy.yml', lambda w, f: w['jobs']['build']['steps'][5]['with'].update({'path': 'site'})),
    'an action by tag': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][0].update({'uses': 'actions/checkout@v7'})),
    'a third job': edit('site-deploy.yml', lambda w, f: w['jobs'].update({'extra': {'runs-on': 'ubuntu-latest', 'steps': [{'run': 'true'}]}})),
    # Any job with an environment, in any workflow.
    'npm test in the signing job': edit('ci.yml', lambda w, f: w['jobs']['sign']['steps'].append({'run': 'npm test'})),
    'npm ci in the signing job': edit('ci.yml', lambda w, f: w['jobs']['sign']['steps'].append({'run': 'cd site\nnpm ci'})),
    # The deploy package and its lockfile.
    'another wrangler version': edit_json('site/deploy/package.json', lambda d: d.update({'dependencies': {'wrangler': '4.0.1'}})),
    'a second dependency': edit_json('site/deploy/package.json', lambda d: d['dependencies'].update({'astro': '7.0.0'})),
    'a script in the deploy package': edit_json('site/deploy/package.json', lambda d: d.update({'scripts': {'postinstall': 'node x.js'}})),
    'an override in the deploy package': edit_json('site/deploy/package.json', lambda d: d.update({'overrides': {'esbuild': '0.2.0'}})),
    'a package the site lockfile lacks': edit_json('site/deploy/package-lock.json', lambda d: d['packages'].update({'node_modules/evil': {'version': '1.0.0', 'resolved': 'https://registry.npmjs.org/evil/-/evil-1.0.0.tgz', 'integrity': 'sha512-x'}})),
    'another version of a package': edit_json('site/deploy/package-lock.json', lambda d: d['packages']['node_modules/esbuild'].update({'version': '0.1.1'})),
    'another integrity': edit_json('site/deploy/package-lock.json', lambda d: d['packages']['node_modules/esbuild'].update({'integrity': 'sha512-z'})),
    'another address': edit_json('site/deploy/package-lock.json', lambda d: d['packages']['node_modules/esbuild'].update({'resolved': 'https://example.com/esbuild.tgz'})),
    'a lockfile for another package': edit_json('site/deploy/package-lock.json', lambda d: d['packages'][''].update({'dependencies': {'wrangler': '3.0.0'}})),
    'no deploy lockfile': lambda w, f: f.pop('site/deploy/package-lock.json'),
    # G-07: the pilot addresses never come from a variable.
    'the pilot inbox from a variable': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][4]['env'].update({'PILOT_TO': '${{ vars.SITE_PILOT_TO }}'})),
    'the pilot sender from a variable': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][4]['env'].update({'PILOT_FROM': '${{ vars.SITE_PILOT_FROM }}'})),
    'the pilot inbox by index': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][4]['env'].update({'PILOT_TO': "${{ vars['SITE_PILOT_TO'] }}"})),
    'the pilot inbox from a variable elsewhere': edit('ci.yml', lambda w, f: w['jobs']['site']['steps'].append({'run': 'echo ${{ vars.SITE_PILOT_TO }}'})),
    'the pilot addresses missing': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][4].pop('env')),
    'the pilot inbox from another secret': edit('site-deploy.yml', lambda w, f: w['jobs']['deploy']['steps'][4]['env'].update({'PILOT_TO': '${{ secrets.OTHER }}'})),
}


def self_test():
    workflows, files = sample()
    if check(workflows, files):
        sys.exit(f"Self-test: the made-up good sample failed: {check(workflows, files)}")
    for label, change in MUST_FAIL.items():
        workflows, files = sample()
        change(workflows, files)
        if not check(workflows, files):
            sys.exit(f"Self-test: the check passed a sample with {label}.")
    return len(MUST_FAIL)


def load(root):
    folder = root / '.github/workflows'
    workflows = {p.name: yaml.safe_load(p.read_text()) for p in sorted(list(folder.glob('*.yml')) + list(folder.glob('*.yaml')))}
    files = {}
    for path in ('site/package.json', 'site/package-lock.json', 'site/deploy/package.json', 'site/deploy/package-lock.json'):
        if (root / path).exists():
            files[path] = (root / path).read_text()
    return workflows, files


def main():
    root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '.')
    cases = self_test()
    bad = check(*load(root))
    for line in bad:
        print(f"::error::{line}")
    if bad:
        sys.exit(1)
    print(f"Self-test: the good sample passed and all {cases} bad ones failed")
    print("no job with an environment runs a package's code, and the site deploy runs Wrangler alone from the reviewed lockfile")


if __name__ == '__main__':
    main()
