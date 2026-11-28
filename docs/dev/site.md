# The public website (ST-124)

`site/` is ScreenTrail's public website: the home page (the owner's approved draft, artifact 740bacdb),
a security and privacy page, the privacy notice, an accessibility statement, the pilot form's two answer
pages and a 404. It is separate from the web admin in `web/`, which is the product.

## Why it is built this way

Decided 2026-11-18 after a review of what could go wrong with a security product's website:

| Risk | What the site does about it |
|---|---|
| A compromised npm package puts a script or tracker in the pages | The pages need no inline code, and `scripts/check-dist.mjs` fails the build if one has an inline script or style, an event handler attribute, or loads anything from another site. The CSP below would also block it in the browser. |
| Visitors tracked by third parties | Fonts are served from the site (`@fontsource`), there is no analytics, and no cookies, so there is no cookie banner. The browser tests fail on any request that leaves the site. |
| Cross-site scripting | `Content-Security-Policy` with `default-src 'none'`, scripts and styles from the site only, and Trusted Types required. `src/scripts/home.ts` never writes HTML from a string, and ESLint forbids `innerHTML`. |
| Spam or abuse through the form | Same-origin check, a hidden honeypot field, a size limit, strict field checks and a rate limit of five posts a minute per address. No third-party CAPTCHA, so no third-party script; Turnstile can be added if spam gets through. |
| The form leaking what people typed | One plain-text email to one fixed inbox (the binding cannot send anywhere else). Nothing is stored; only an error code is ever logged; Cloudflare's request logs are off. |
| A stranger's pull request deploying, or reading the token | Deploys come only from `main` (`.github/workflows/site-deploy.yml`), with the token in the `site-production` environment. Pull requests run the Site job in `ci.yml`, which has no secrets. Actions in the deploy workflow are pinned to commits. |
| A compromised npm package reaching the deploy token | The workflow has two jobs. The build job installs, tests and builds with no environment and no secret, and hands over `dist/` alone. The deploy job, the only one in `site-production`, installs Wrangler on its own from `site/deploy/` with `npm ci --ignore-scripts` (its lockfile holds only packages `site/package-lock.json` already pins, at the same versions and hashes) and runs nothing else. `scripts/ci/check_site_deploy.py` holds that shape, and refuses an install script, test or build in any job with an environment. Keep `site/deploy/package.json`'s Wrangler at the same version as `site/package.json`'s: after a Dependabot update, run `npm install --package-lock-only --ignore-scripts` in `site/deploy/` with the new version. |
| Claims the product does not keep | The copy is held to `docs/STATUS.md`: no redaction guarantee, targets marked as targets, the ConnectWise and Hudu connections called built but not yet checked against live accounts. Change the copy when the status changes. |
| Being tied to one host | The output is plain files plus a 120-line Worker; moving to another host is an afternoon. |

## Working on it

```sh
cd site
npm ci
npm run dev        # http://localhost:4321, live reload (no Worker: the form will 404)
npm test           # the Worker's checks and the header policy (Vitest)
npm run build      # astro check, the build, then scripts/check-dist.mjs
npm run e2e        # Playwright on wrangler dev: axe, no off-site requests, 400% zoom, the form
npx wrangler dev   # the built site with the real headers and the Worker, on http://localhost:8787
```

- **Copy and layout:** `src/pages/*.astro`; the home page's styles are in `src/styles/home.css`, the
  rest in `src/styles/global.css`. No `style=""` attributes: the CSP forbids them. Use a class.
- **Brand colours** come from `shared/design/tokens.json`: `npm run codegen` in `shared/design` writes
  `src/styles/tokens.g.css` here as well as the app's files, and CI fails if it is stale.
- **The site's facts** (address, contact and security emails, repository) are in `src/site.config.ts`.
- **Screenshots** of the app are in `src/assets/screens/`. The app draws them for the site in CI: the
  `gallery-screenshots` artifact of a Client (Windows) run holds `site-*-light.png`, at 1280 by 800 and
  twice the pixels so they stay sharp (`ShellWindow.Renders.cs`, `SiteAsync`). Download the artifact
  and run `node scripts/crop-screens.mjs <folder>`: it keeps the Review screen and the capture panel
  whole and cuts the three small cards as the same region of three screens, so they match in size.
  Sample data only. Run it again when the screens change.
- **The share card and icons**: `node scripts/make-images.mjs` redraws `src/assets/og.png`,
  `public/favicon.ico` and `public/apple-touch-icon.png`.
- **Headers** are in `public/_headers`; `tests/headers.test.ts` keeps them strict.

## First deploy (owner)

Do these once, in order. Nothing deploys until step 6.

1. **Domain and Cloudflare.** Buy the domain (checking the name is clear to use as a trademark first),
   create a Cloudflare account with two-factor sign-in, and add the domain to it (Cloudflare Registrar,
   or point the registrar's name servers at Cloudflare).
2. **Harden the domain** in Cloudflare's DNS settings:
   - turn on **DNSSEC** (and add the DS record at the registrar if it is not Cloudflare), and turn on the
     registrar lock;
   - add a **CAA** record: `0 issue "letsencrypt.org"`, `0 issue "pki.goog"` and `0 issue "digicert.com"`
     (the authorities Cloudflare uses), and `0 iodef "mailto:<security address>"`;
   - until a mail provider is chosen for the product, add `v=spf1 -all` as a TXT record and
     `v=DMARC1; p=reject; rua=mailto:<your address>` as a TXT record on `_dmarc`, so nobody can send mail
     as the domain. Email Service (step 3) adds its own SPF and DKIM records; keep DMARC at `p=reject`.
3. **Email for the form.** In Cloudflare, turn on Email Service sending for the domain
   (`npx wrangler email sending enable <domain>`), and choose two addresses: a sender on the domain (for
   example `pilot-form@<domain>`) and the inbox that receives enquiries.
4. **A deploy token.** The site has a Cloudflare account of its own, holding no other Worker, and the
   product's host names live outside it (DEC-10): a token for Workers reaches every Worker in its
   account. In that account, create a **custom** API token with one permission, Account, Workers
   Scripts, Edit, limited to this account (not the "Edit Cloudflare Workers" template, which also
   grants Workers Routes on the zone and more). Note the account ID. If Wrangler cannot attach the custom
   domain with this token on the first deploy, attach it to the Worker once by hand (Workers, the
   Worker, Settings, Domains and Routes).
5. **The GitHub environment.** In the repository's Settings, Environments, create `site-production`:
   - Deployment branches: **main only**. Optionally add yourself as a required reviewer.
   - Secrets: `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, and the pilot form's sender and inbox as
     `SITE_PILOT_FROM` and `SITE_PILOT_TO`. Secrets, not variables: the deploy log shows the mail
     binding's addresses, and only a secret's value is masked there.
   - And as **repository** variables (Settings, Secrets and variables, Actions, Variables), because the
     build job names no environment: `SITE_URL` (for example `https://screentrail.com`, no trailing
     slash), `SITE_CONTACT_EMAIL`, `SITE_SECURITY_EMAIL`. All three are printed on the site anyway.

   This changes none of the three settings that protect the laptop runner.
6. **Turn it on.** Add the repository variable `SITE_DEPLOY` = `true`, then run "Deploy site" from the
   Actions tab (or merge any change under `site/`). The job refuses to deploy while any placeholder
   address is left.
7. **Check it:** open the site, send yourself a pilot enquiry, and run the address through
   [securityheaders.com](https://securityheaders.com) and
   [hstspreload.org](https://hstspreload.org). Submit the domain to the HSTS preload list only once you
   are sure every subdomain will be HTTPS for good (the header already asks for it).

## Dependencies

Dependabot opens one grouped pull request a week for `site/`'s npm packages, which the Site job builds
and tests, and one for `site/deploy/` (Wrangler alone, see above). It does not update GitHub Actions:
those updates would edit the laptop runner's workflows. `.github/dependabot.yml` lists the other
folders it watches.

## What the site does not do yet

- No blog, changelog or documentation pages.
- No dark theme: the site is light, as the owner chose for the app (2026-11-13). It pins
  `data-theme="light"` so the shared tokens' dark values never apply.
- The privacy notice covers the website; the product's privacy policy and DPA come with the pilot
  agreement (ST-111), and both deserve a lawyer's read.
