# VPS deploy user — one-time setup for `web-deploy-images.yml`

The `deploy` job in [`.github/workflows/web-deploy-images.yml`](../../.github/workflows/web-deploy-images.yml)
ships the built Docker images to production over SSH and reloads compose. It
authenticates as a **dedicated, least-privilege deploy user** — never `root`,
never an existing personal/admin key. This is a one-time setup on the VPS;
after it's done, every push to `main` that touches `backend/**`, `frontend/**`,
`marketing/**`, `rulebooks/**`, `templates/**`, or `deploy/vps/**` deploys
itself.

## Why not root

Giving a CI runner root SSH access to production means any workflow
compromise (a malicious PR, a poisoned dependency, a leaked Actions secret)
is a full box compromise. A user that can only `docker load` / `docker
compose up` in one directory bounds the blast radius to "can replace the
RadioPad containers" — bad, but recoverable, and nothing else on the host is
reachable through it.

## 1. Create the user (on the VPS, as root)

```bash
useradd -m -s /bin/bash radiopad-deploy
usermod -aG docker radiopad-deploy
```

The `docker` group grants `docker load` / `docker compose up` without sudo —
confirm that's still the narrowest group that works before widening it.

## 2. Generate a dedicated deploy key (do NOT reuse an existing key)

Run this on your own machine, not the VPS — the private half goes straight
into a GitHub secret and should never touch disk on the server:

```bash
ssh-keygen -t ed25519 -C "radiopad-deploy-ci" -f ./radiopad_deploy_key -N ""
```

This produces `radiopad_deploy_key` (private) and `radiopad_deploy_key.pub`
(public).

## 3. Authorize the public key on the VPS

```bash
# On the VPS, as root:
mkdir -p /home/radiopad-deploy/.ssh
cat >> /home/radiopad-deploy/.ssh/authorized_keys << 'EOF'
<paste radiopad_deploy_key.pub contents here>
EOF
chmod 700 /home/radiopad-deploy/.ssh
chmod 600 /home/radiopad-deploy/.ssh/authorized_keys
chown -R radiopad-deploy:radiopad-deploy /home/radiopad-deploy/.ssh
```

## 4. Grant read/write on the compose directory

The production compose project lives at `/opt/docker/radiopad` (bare
`docker-compose.yml` + `.secrets.env`, no `src/` subfolder — verified against
the live host 2026-07-24; older docs referencing `/opt/radiopad` are stale).

```bash
chown -R radiopad-deploy:radiopad-deploy /opt/docker/radiopad
```

`docker load` only needs the `docker` group; this chown lets the deploy user
`cd /opt/docker/radiopad && docker compose up -d` without sudo. It does NOT
need to read `.secrets.env`'s contents to do that — `docker compose up`
reads it as the `radiopad-deploy` user, same as any file it owns.

## 5. Verify the login works before wiring CI to it

```bash
ssh -i ./radiopad_deploy_key radiopad-deploy@<VPS_HOST> 'docker ps && cd /opt/docker/radiopad && docker compose ps'
```

## 6. Add the three GitHub secrets

```bash
gh secret set VPS_HOST --repo jerryboganda/radiopad --body "<vps-ip-or-hostname>"
gh secret set VPS_DEPLOY_USER --repo jerryboganda/radiopad --body "radiopad-deploy"
gh secret set VPS_DEPLOY_SSH_KEY --repo jerryboganda/radiopad < ./radiopad_deploy_key
```

Then delete the local private key file — it now lives only in the GitHub
secret store and the VPS's `authorized_keys`.

```bash
rm ./radiopad_deploy_key ./radiopad_deploy_key.pub
```

Until these three secrets exist, the `deploy` job in `web-deploy-images.yml`
skips itself (prints a notice, does not fail) — the manual fallback
documented at the top of that file still works exactly as before.

## Rotating the key

Repeat steps 2–3 with a new keypair, add the new public key to
`authorized_keys` alongside the old one, update the `VPS_DEPLOY_SSH_KEY`
secret, confirm a deploy run succeeds, then remove the old public key from
`authorized_keys`.
