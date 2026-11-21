# RadioPad — Hostinger web hosting (website + admin web app)

**Status:** Current  ·  **Owner:** Operations  ·  **Last Updated:** 2026-11-21

The two static surfaces are served from Hostinger web hosting. The API is NOT
here (Hostinger web hosting cannot run .NET, background jobs or WebSockets) —
it runs on the VPS at `https://api.radiopadstudio.com`, see
[`deploy/vps/README.md`](../vps/README.md).

| Site | Source | Build | Hostinger folder |
|---|---|---|---|
| `radiopadstudio.com` (+ `www`) | `marketing/` (Astro) | `pnpm --filter @radiopad/marketing build` → `marketing/dist` | `~/domains/radiopadstudio.com/public_html` |
| `admin.radiopadstudio.com` | `frontend/` web surface | `NEXT_PUBLIC_API_BASE=https://api.radiopadstudio.com pnpm --filter @radiopad/frontend build:web` → `frontend/out-web` | `~/domains/admin.radiopadstudio.com/public_html` |

## Account / server

- hPanel account that owns the `radiopadstudio.com` registration; plan
  **Unlimited Web Hosting** (also hosts `radiopad.eu`). Both RadioPad sites were
  created as empty **PHP/HTML** sites.
- Server `46.202.138.251` (LiteSpeed). SSH: `ssh -p 65002 u372263031@46.202.138.251`.
- Deploy key: `radiopad-hostinger-deploy` (hPanel → Website → Advanced → SSH
  access → SSH keys). Private half = GitHub secret `HOSTINGER_SSH_KEY`
  (and `~/.ssh/radiopad_hostinger_deploy` on the operator laptop, SSH alias
  `hostinger-radiopad`).
- Host key (ED25519) `SHA256:MMyZJyHe7HjLzIOHkHaTtOnG3oFdd5xY2UkBDXQUQsc` —
  pinned in the workflow; a mismatch aborts the deploy.
- SSL: Hostinger "Lifetime SSL" (Let's Encrypt, auto-renewed by Hostinger) on
  each site. Required because Cloudflare runs in **Full (strict)**.

## Deploying

Automatic: any push to `main` touching `frontend/**`, `marketing/**`,
`deploy/hostinger/**`, `package.json`, `pnpm-lock.yaml` or
`pnpm-workspace.yaml` runs [`hostinger-deploy.yml`](../../.github/workflows/hostinger-deploy.yml):
builds both sites on GitHub Actions, copies the `.htaccess` files below into
the build output, `rsync --delete`s each into its `public_html`, then
smoke-tests both hosts directly against the Hostinger IP. Re-run any time via
`workflow_dispatch`. Never build on the laptop or the server (CLAUDE.md).

## `.htaccess`

LiteSpeed reads Apache `.htaccess`. These mirror the old nginx configs:

- [`web.htaccess`](web.htaccess) — security headers; CSP `connect-src` allows
  `https://api.radiopadstudio.com wss://api.radiopadstudio.com`; SPA fallback
  to `/index.html`; immutable cache for `/_next/static/`; http→https (honours
  Cloudflare's `X-Forwarded-Proto`).
- [`marketing.htaccess`](marketing.htaccess) — security headers, Astro
  `404.html`, http→https. No `/api` proxy: clients call the API host directly.

If the API host ever changes, update `API_BASE` in the workflow **and** the
CSP in `web.htaccess`, then also the API's `RADIOPAD_CORS_ORIGINS`.

## Cloudflare DNS (zone `radiopadstudio.com`)

| Record | Target | Proxy |
|---|---|---|
| `radiopadstudio.com` A | `46.202.138.251` (Hostinger) | Proxied |
| `www` CNAME | `radiopadstudio.com` | Proxied |
| `admin` A | `46.202.138.251` (Hostinger) | Proxied |
| `api` A | `185.252.233.186` (VPS) | Proxied |
| MX `mx1`/`mx2.hostinger.pk`, SPF TXT, `autoconfig`, `autodiscover` | Hostinger mail | DNS only — never proxy |

## Checks

```bash
for u in https://radiopadstudio.com/ https://www.radiopadstudio.com/ \
         https://admin.radiopadstudio.com/login/ https://radiopadstudio.com/nope; do
  curl -s -o /dev/null -w "%{http_code} $u\n" "$u"     # 200 200 200 404
done
# Origin directly (bypassing Cloudflare):
curl -s -o /dev/null -w "%{http_code}\n" --resolve admin.radiopadstudio.com:443:46.202.138.251 https://admin.radiopadstudio.com/
```

## Rollback

Re-run `hostinger-deploy` from the last good commit (`workflow_dispatch` on
that ref, or revert + push). The previous files are not kept on the server
(`rsync --delete`); Hostinger's daily backups (hPanel → Backups) are the
fallback.
