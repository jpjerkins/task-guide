# First deploy — task-guide to pi5

Walking skeleton (#51), step 6. Run on **pi5** as `philj` unless a step says otherwise.

Nothing here has been executed. Every command is written to be checked before it is run, and
each step ends with a verification whose expected output is stated. If a verification does not
match, **stop** — the next step assumes it passed.

**Facts this deploy depends on**

| Thing | Value | Why |
|---|---|---|
| Service UID/GID | `50013` | vault-t2 requires one UID per service from the reserved range 50000–50099 |
| Container port | `8007` | `Program.cs` hardcodes `http://0.0.0.0:8007`. **Not 8080** |
| Published port | `8007`, host mode | tailnet-only, TLS terminated by Tailscale Serve |
| Data dir | `/mnt/data/task-guide` → `/data` | DCM `data_dir: true` |
| Secrets | `pushover_user`, `pushover_token_task-guide` | vault-t2 Tier 2, read over FUSE |
| Update order | `stop-first` | single writer; the store is memory-authoritative |

No OS user needs creating. The vault and the kernel both check the UID numerically — this is why
the vault docs use `sudo -u '#50013'`.

---

## Step 1 — Confirm 50013 is actually free

`50013` was chosen without sight of the live ACL. Verify before relying on it.

```bash
sudo cat /etc/vault-t2/acl.yaml | grep -n 50013
grep -rn "vault_uid" ~/dev/dcm/registry.yaml
```

**Expected:** no match for `50013` in either. If something already claims it, pick another free
number in 50000–50099 and substitute it everywhere below — including the Dockerfile, which bakes
it in and would need a rebuild.

---

## Step 2 — Store the Pushover secrets (skip if already stored)

```bash
t2-list | grep -E "pushover_user|pushover_token_task-guide"
```

**Expected:** both names listed. If either is missing:

```bash
# Paste the value at the prompt; it is not echoed and does not enter shell history.
read -rs SECRET && printf '%s' "$SECRET" | t2-set pushover_user   && unset SECRET
read -rs SECRET && printf '%s' "$SECRET" | t2-set pushover_token_task-guide && unset SECRET
```

---

## Step 3 — Grant UID 50013 read access to both secrets

Preferred, because it keeps the UID declared with the service rather than in a file someone has
to remember. In DCM's `registry.yaml`, on the `task-guide` service:

```yaml
task-guide:
  vault_uid: 50013
  vault_secrets:
    - pushover_user
    - pushover_token_task-guide
```

Then regenerate:

```bash
sudo cp /etc/vault-t2/acl.yaml /etc/vault-t2/acl.yaml.bak   # keep a way back
dcm secrets acl-generate | sudo vault-t2-acl-update
```

**Verify — this is the real test, not that the file looks right:**

```bash
sudo -u '#50013' cat /run/vault-t2-fs/pushover_user | head -c 4; echo
```

**Expected:** the first four characters of the user key. `Permission denied` means the ACL did not
take.

<details>
<summary>Fallback if <code>acl-generate</code> is not wired up for this service</summary>

```bash
sudo $EDITOR /etc/vault-t2/acl.yaml
```
Add `50013` under both `pushover_user` and `pushover_token_task-guide`, then re-run the verify
above.
</details>

---

## Step 4 — Map the secrets to environment variables

The app binds `Pushover:Token` and `Pushover:UserKey` from configuration, so the envfile pattern
needs **no code change**. Double underscore is .NET's separator for nested config keys.

```bash
sudo cp /etc/vault-t2/envfiles.yaml /etc/vault-t2/envfiles.yaml.bak
sudo $EDITOR /etc/vault-t2/envfiles.yaml
```

Add:

```yaml
task-guide:
  uid: 50013
  env:
    Pushover__Token: pushover_token_task-guide
    Pushover__UserKey: pushover_user
```

> **This restart affects more than task-guide.** `vault-t2-fuse` reads `envfiles.yaml` once at
> daemon start, and every Tier 2 service reads its secrets through this mount. Expect a brief
> window where they cannot. Do this when a short interruption is acceptable.

```bash
sudo systemctl restart vault-t2-fuse
systemctl is-active vault-t2-fuse
```

**Expected:** `active`.

```bash
sudo -u '#50013' cat /run/vault-t2-fs/envfiles/task-guide
```

**Expected:** two lines, `Pushover__Token=...` and `Pushover__UserKey=...`, with real values.

---

## Step 5 — Create the data directory with the right owner

This is the step most likely to be skipped and most likely to bite. The container runs non-root as
50013; a directory it cannot write makes every `POST` return 503.

```bash
sudo mkdir -p /mnt/data/task-guide
sudo chown 50013:50013 /mnt/data/task-guide
sudo chmod 750 /mnt/data/task-guide
sudo -u '#50013' touch /mnt/data/task-guide/.probe && sudo -u '#50013' rm /mnt/data/task-guide/.probe && echo "writable by 50013"
```

**Expected:** `writable by 50013`.

> If DCM's `data_dir: true` creates this directory itself during deploy, it may reset ownership.
> Re-run the `chown` and the probe **after** step 6 and before trusting the service.

---

## Step 6 — Register and deploy

Registration uses a `build` block so pi5 builds the image natively on ARM64. Trust
`lib/registry.py` over the documented `ServiceSpec` — the docs are known incomplete.

```bash
cd ~/dev && git clone <this-repo> task-guide || (cd task-guide && git pull)
cd ~/dev/task-guide && git checkout walking-skeleton
```

Register with:

- `build` block pointing at the repo root (the `Dockerfile` is there)
- `data_dir: true`
- ports: `{"target": 8007, "published": 8007, "protocol": "tcp", "mode": "host"}`
- `user: "50013:50013"`
- `env_file: /run/vault-t2-fs/envfiles/task-guide`
- `update_config: {"order": "stop-first"}`
- `vault_uid: 50013` and `vault_secrets` as in step 3

**Verify:**

```bash
curl -s http://localhost:8007/health
```

**Expected:** `{"ok":true,...,"storage":{"readable":true,"writable":null},...}`

`writable` is `null` before the first write — that is correct, not a fault. It means "nothing
observed yet" rather than an unverified `true`.

---

## Step 7 — Prove the vertical slice on the device

```bash
curl -s -i -X POST http://localhost:8007/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":"first task on pi5","duration":10}'
```

**Expected:** `HTTP/1.1 201 Created` with a `t_`-prefixed ULID and a `Location` header.
**A 503 means the data directory is not writable by 50013 — go back to step 5.**

```bash
curl -s http://localhost:8007/api/tasks
cat /mnt/data/task-guide/tasks.json
curl -s http://localhost:8007/health
```

**Expected:** the task in the list, on disk, and health now reporting `"writable":true`.

---

## Step 8 — The Pushover push

This is the last unproven integration point in #51 and the only one that cannot be verified from
a terminal: **watch your phone.**

The tick loop fires roughly every 30 s and sends exactly one push, once at least one Task exists —
then never again for the life of the container. A task exists as of step 7, so the next tick
should deliver it.

```bash
sleep 35 && dcm logs task-guide 2>/dev/null | tail -20
```

**Expected:** one send, and one notification on your phone.

**If the log says `Pushover is not configured (missing Token or UserKey)`** the envfile did not
reach the container. Check:

```bash
dcm exec task-guide env | grep -c Pushover__     # expect 2
```

Being explicit about what this proves: a push landing on your phone is the *first* evidence that
Pushover works at all. Everything before this — the client, the one-push limit, the missing-token
no-op — was tested only against an unconfigured token.

---

## Step 9 — Tailscale Serve

```bash
sudo tailscale serve --bg --https=443 http://localhost:8007
tailscale serve status
```

**Expected:** a mapping from the MagicDNS name to `localhost:8007`.

The client-facing name is the MagicDNS name `pi5.<tailnet>.ts.net` — **not** `pi5.local`, which
does not resolve reliably (it does not resolve from the Mac at all today).

**Funnel is an explicit never.** Do not add `--funnel`; this service has no auth and is gated
entirely at the network layer.

Verify from another tailnet device:

```bash
curl -s https://pi5.<tailnet>.ts.net/health
```

---

## Rolling back

```bash
dcm stop task-guide
sudo cp /etc/vault-t2/envfiles.yaml.bak /etc/vault-t2/envfiles.yaml
sudo cp /etc/vault-t2/acl.yaml.bak /etc/vault-t2/acl.yaml
sudo systemctl restart vault-t2-fuse
```

`/mnt/data/task-guide` is left alone deliberately — it holds the only copy of anything captured.

---

## Known-unproven going in

- **Ownership of `/mnt/data/task-guide`.** Could not be tested locally: Docker Desktop on macOS
  does not enforce host UID ownership across its VM file sharing, so a non-root container writing
  to a host-owned directory succeeds on the Mac and may not on Linux. Step 5 exists for this.
- **Playwright on ARM64/Debian 12** — verified in research, never executed. Not needed for this
  deploy.
- **Backups.** `docs/research/dcm-dotnet-deployment.md` §6 recorded the `/mnt/data` → `/mnt/backup`
  routine as broken; that is corrected as of 2026-08-26 but **user-reported, not device-verified**.
  Confirm `/mnt/data/task-guide` is actually included before treating captured data as safe, and
  before the Restore drill (#49).
