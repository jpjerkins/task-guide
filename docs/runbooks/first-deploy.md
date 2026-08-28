# First deploy — task-guide to pi5

Walking skeleton (#51), step 6. Run on **pi5** as `philj` unless a step says otherwise.

**Executed end to end on 2026-08-27**, and corrected against what actually happened. Each step
ends in a verification with its expected output; if one does not match, **stop** — the next step
assumes it passed.

> **Two things this runbook originally got wrong**, both fixed below:
> 1. `env_files` pointing at a vault-t2 FUSE path **cannot work**. Compose resolves `env_file:`
>    client-side at deploy time as root, and vault-t2 denies even root. See step 6.
> 2. `sudo -u '#50013'` fails on pi5 — it runs **sudo-rs**, which does not support the `#uid`
>    form. Use `setpriv` instead.

Modelled on two services in `~/dev/dcm/registry.yaml`: `tto-web-api` for the shape (a .NET
service on Swarm with a `/data` bind mount), and **`gmail-mcp` for secrets** (bind-mount
`/run/vault-t2-fs` read-only, read it in-process as the service's own UID).

**Facts this deploy depends on**

| Thing | Value | Why |
|---|---|---|
| Service UID/GID | `50013` | vault-t2 requires one UID per service from 50000–50099, declared in `acl.yaml` |
| Container port | `8007` | `Program.cs` hardcodes `http://0.0.0.0:8007`. **Not 8080** |
| Published port | `8007`, host mode | tailnet-only, TLS terminated by Tailscale Serve |
| Data dir | `/mnt/data/task-guide` → `/data` | explicit bind mount — see step 1 |
| Secrets | `pushover_user`, `pushover_token_task-guide` | vault-t2 Tier 2, read over FUSE |
| Runtime | `swarm` | DCM's default `artifact_format` |

No OS user needs creating. The vault and the kernel both check the UID numerically. The vault docs
use `sudo -u '#50013'` for this, but **pi5 runs sudo-rs, which does not support the `#uid` form** —
every such command below uses `setpriv --reuid/--regid` instead.

---

## What DCM does and does not own

Worth stating up front, because it determines which steps are API calls and which are not:

| Concern | Owner | How |
|---|---|---|
| Service registration, build, deploy | **DCM** | `POST /deploy` with a `ServiceSpec` |
| vault-t2 `acl.yaml` | **DCM** | generated from `registry.yaml`'s `vault_uid`/`vault_secrets` |
| ACL drift detection | **DCM** | `dcm secrets check` |
| `~/secrets/<name>.env` | **DCM** | rendered from `secret_env` mappings |
| vault-t2 `envfiles.yaml` | **not DCM** | no reference anywhere in the DCM source; edited by hand |
| Data dir on **Swarm** | **not DCM** | `lib/deployer.py` creates it only when `not swarm_runtime` |

The last two are the only places this runbook reaches past the API, and both are deliberate.

### Why the order below looks odd

There is a genuine circularity: `dcm secrets acl-generate` derives the ACL from `registry.yaml`'s
`vault_uid`/`vault_secrets`, and those only exist **after** the service is registered. So the
service must be deployed before its secrets can be authorised — which means its first start
happens with Pushover unconfigured.

That is survivable but must be closed deliberately, because the tick loop's one-push flag counts
an unconfigured no-op as "attempted". A container that boots without credentials will never push,
even after the secrets appear. **Step 7 restarts it for exactly this reason — do not skip it.**

---

## Step 1 — Create the data directory

**DCM will not create this.** `lib/deployer.py:925` guards data-dir creation with
`if spec.data_dir and not swarm_runtime`, and this service is Swarm. `data_dir: true` is still
passed for registry correctness, but the directory and its ownership are ours.

The container runs non-root as 50013; a directory it cannot write makes every `POST` a 503.

```bash
sudo mkdir -p /mnt/data/task-guide
sudo chown 50013:50013 /mnt/data/task-guide
sudo chmod 750 /mnt/data/task-guide
# pi5 runs sudo-rs, which rejects `sudo -u '#50013'` with "unknown user #50013". setpriv is
# the portable equivalent and needs no OS user to exist.
sudo setpriv --reuid=50013 --regid=50013 --clear-groups touch /mnt/data/task-guide/.probe \
  && sudo setpriv --reuid=50013 --regid=50013 --clear-groups rm /mnt/data/task-guide/.probe \
  && echo "writable by 50013"
```

**Expected:** `writable by 50013`.

---

## Step 2 — Confirm 50013 is free

`50013` was chosen without sight of the live ACL.

```bash
dcm secrets acl-generate | grep -n 50013
sudo grep -n 50013 /etc/vault-t2/acl.yaml   # sudo-rs is fine here; only -u '#uid' is unsupported
grep -n "vault_uid" ~/dev/dcm/registry.yaml
```

**Expected:** no existing service claims it. If one does, pick another free number in 50000–50099
and substitute everywhere — **including the Dockerfile, which bakes it in and needs a rebuild.**

---

## Step 3 — Store the Pushover secrets (skip if present)

```bash
t2-list | grep -E "pushover_user|pushover_token_task-guide"
```

**Expected:** both listed. If either is missing:

```bash
# Value is not echoed and does not enter shell history.
read -rs SECRET && printf '%s' "$SECRET" | t2-set pushover_user             && unset SECRET
read -rs SECRET && printf '%s' "$SECRET" | t2-set pushover_token_task-guide && unset SECRET
```

---

## Step 4 — Register and deploy

Use the **HTTP API**, not the `dcm_deploy` MCP tool: that tool's arguments omit `mounts`,
`env_files` and `runtime.user`, all of which this service needs. `POST /deploy` takes the full
`ServiceSpec` (`lib/registry.py:366`).

Get the repo onto pi5 first — the build runs there, natively on ARM64. **`walking-skeleton` has
never been pushed**, so there is nothing to clone; rsync the working tree from the Mac instead
(excluding build outputs the image must produce itself):

```bash
# from the repo root on the Mac
rsync -az --delete \
  --exclude 'bin/' --exclude 'obj/' --exclude 'node_modules/' --exclude '.git/' \
  --exclude 'src/TaskGuide.Api/wwwroot/' \
  ./ pi5:~/dev/task-guide/
```

DCM's API listens on **8765** (`api/server.py:40`) and requires a bearer token read from
`~/secrets/dcm_api_key`. If it is missing, the API answers 503 with `Run: dcm api generate-key`.

```bash
DCM_TOKEN=$(cat ~/secrets/dcm_api_key)

curl -s -X POST http://localhost:8765/deploy \
  -H "Authorization: Bearer $DCM_TOKEN" \
  -H "Content-Type: application/json" \
  -d @- <<'JSON'
{
  "name": "task-guide",
  "description": "Opportunistic task reminder — walking skeleton (#51)",
  "artifact_format": "swarm",
  "managed": true,
  "upgrade_strategy": "build",
  "data_dir": true,
  "build": {
    "context": "/home/philj/dev/task-guide",
    "dockerfile": "Dockerfile",
    "target_image": "localhost:5000/task-guide",
    "platform": "linux/arm64"
  },
  "ports": [
    { "target": 8007, "published": 8007, "protocol": "tcp", "mode": "host" }
  ],
  "mounts": [
    { "type": "bind", "target": "/data", "source": "/mnt/data/task-guide",
      "read_only": false, "purpose": "data" },
    { "type": "bind", "target": "/run/vault-t2-fs", "source": "/run/vault-t2-fs",
      "read_only": true, "purpose": "secret" }
  ],
  "environment": {
    "Storage__DataDir": "/data"
  },
  "runtime": { "user": "50013:50013" },
  "vault_uid": 50013,
  "vault_secrets": ["pushover_user", "pushover_token_task-guide"],
  "update_policy": { "order": "stop-first" },
  "replicas": 1
}
JSON
```

`stop-first` matters: the store is memory-authoritative with a single writer, so two replicas
overlapping would have two processes owning the same file.

**There is deliberately no `env_files` here** — see step 6 for why it cannot work. The
`/run/vault-t2-fs` bind mount replaces it, and `purpose` must be `secret` (singular); the API
rejects `secrets` with a 422 enum error.

**If the service is already registered**, `POST /deploy` answers
`{"detail":"Service 'task-guide' already exists in registry"}`. Amend it in place instead — this
does not re-render the stack file, so follow it with `POST /upgrade/task-guide`:

```bash
curl -s -X PATCH http://localhost:8765/registry/task-guide \
  -H "Authorization: Bearer $DCM_TOKEN" -H "Content-Type: application/json" \
  -d '{"env_files": [], "mounts": [ ... ]}'
```

---

## Step 5 — Apply the vault ACL

`vault_uid`/`vault_secrets` land in `registry.yaml` at registration; the ACL is generated from
them rather than hand-written.

```bash
sudo cp /etc/vault-t2/acl.yaml /etc/vault-t2/acl.yaml.bak
dcm secrets acl-generate | sudo vault-t2-acl-update
dcm secrets check
```

**Expected:** `dcm secrets check` reports no drift.

**The real test is not that the file looks right:**

```bash
AS_SVC="sudo setpriv --reuid=50013 --regid=50013 --clear-groups"
$AS_SVC cat /run/vault-t2-fs/pushover_user | head -c 4; echo
$AS_SVC cat /run/vault-t2-fs/envfiles/task-guide | cut -d= -f1   # key names only
```

**Expected:** four characters of the user key, then two lines `Pushover__Token=...` and
`Pushover__UserKey=...` with real values. `Permission denied` means the ACL did not take.

---

## Step 6 — Map the secrets to environment variables

**This is the one step DCM does not own.** There is no `envfiles` handling anywhere in the DCM
source, so this file is edited directly. The app binds `Pushover:Token` / `Pushover:UserKey` from
configuration, and `__` is .NET's nested-key separator, so the names map across untranslated.

### Why compose's `env_file:` cannot be used for this

The original version of this runbook put `/run/vault-t2-fs/envfiles/task-guide` in the service's
`env_files`. That fails, and not merely on ordering:

```
open /run/vault-t2-fs/envfiles/task-guide: no such file or directory
```

Compose resolves `env_file:` **client-side, at deploy time**, as the user running `docker stack
deploy` — root. And vault-t2 serves each envfile to its declared UID *alone*, denying even root:

```
$ sudo cat /run/vault-t2-fs/envfiles/gmail-mcp
cat: Permission denied
```

So the only process permitted to read the file is the service itself, at runtime. Creating the
file earlier would only have turned "no such file" into "permission denied".

`gmail-mcp`, `youtube-mcp` and `shortcuts-api` all solve this the same way: bind-mount
`/run/vault-t2-fs` read-only and read it in-process as their own UID. None uses `env_file`. This
service now does likewise — `Program.cs` calls `AddEnvFile(...)` on the path below, `optional:
true` so that local runs without a FUSE mount still start.

(`tto-web-api` *does* use `env_file`, but pointed at `~/secrets/tto-web-api.env`, which DCM
renders from a `secret_env` mapping. That writes credentials to disk in plaintext — the existing
file is mode `0664`. The FUSE route keeps them off disk entirely, which is why it was chosen.)

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

> **This restart affects every Tier 2 service, not just this one.** `vault-t2-fuse` reads
> `envfiles.yaml` once at daemon start, and all Tier 2 services read their secrets through that
> mount. Do it when a brief interruption is acceptable.

```bash
sudo systemctl restart vault-t2-fuse
systemctl is-active vault-t2-fuse
```

**Expected:** `active`.

---

## Step 7 — Restart so the service picks up its credentials

Its first start had no envfile. Without this it stays unconfigured for the life of the container,
and the one-push flag means it will never send.

```bash
curl -s -X POST http://localhost:8765/restart/task-guide \
  -H "Authorization: Bearer $(cat ~/secrets/dcm_api_key)"

sleep 10
docker exec $(docker ps -qf name=task-guide) \
  sh -c 'cut -d= -f1 /run/vault-t2-fs/envfiles/task-guide'
```

**Expected:** `Pushover__Token` and `Pushover__UserKey`.

**Note the check changed** along with the mechanism. The secrets are no longer container
*environment variables*, so `env | grep -c Pushover__` now correctly returns `0` — the process
reads the mounted file at startup instead. Check the mount, not the environment.

**This restart is still load-bearing** on a clean run. `POST /deploy` registers *and* starts the
service in one call, so the container's first start precedes the ACL and envfile of steps 5–6,
and `AddEnvFile` reads the file only at startup. (If a failed deploy meant the container never
started until after steps 5–6 — as happened on 2026-08-27 — it comes up configured and this
restart is a no-op.)

---

## Step 8 — Confirm the data dir survived

DCM may have touched `/mnt/data/task-guide` during deploy.

```bash
stat -c '%u:%g %a' /mnt/data/task-guide
```

**Expected:** `50013:50013 750`. If not, re-run step 1's `chown` and probe.

---

## Step 9 — Prove the vertical slice on the device

```bash
curl -s http://localhost:8007/health
```

**Expected:** `{"ok":true,...,"storage":{"readable":true,"writable":null},...}`

`writable: null` before the first write is correct, not a fault — it means "nothing observed yet"
rather than an unverified `true`.

```bash
curl -s -i -X POST http://localhost:8007/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":"first task on pi5","duration":10}'
```

**Expected:** `HTTP/1.1 201 Created`, a `t_`-prefixed ULID, a `Location` header.
**A 503 means the data directory is not writable by 50013 — go back to step 1.**

```bash
curl -s http://localhost:8007/api/tasks
cat /mnt/data/task-guide/tasks.json
curl -s http://localhost:8007/health
```

**Expected:** the task in the list, on disk, and health now reporting `"writable":true`.

---

## Step 10 — The Pushover push

The last unproven integration point in #51, and the only one not verifiable from a terminal:
**watch your phone.**

The tick fires roughly every 30 s and sends exactly one push once at least one Task exists, then
never again for the life of the container. A task exists as of step 8.

```bash
sleep 35
docker service logs --tail 30 task-guide_task-guide
```

(`dcm logs` and `dcm exec` do not exist — `bin/` has no such commands. Use `docker service logs`
for Swarm.)

**Expected:** one send, and one notification on your phone.

**If the log says `Pushover is not configured (missing Token or UserKey)`**, the envfile did not
reach the container:

```bash
docker exec $(docker ps -qf name=task-guide) \
  sh -c 'cut -d= -f1 /run/vault-t2-fs/envfiles/task-guide'   # expect both key names
```

Being explicit about what this proves: a push landing on your phone is the **first** evidence that
Pushover works at all. The client, the one-push limit and the missing-token no-op were all tested
only against an unconfigured token.

---

## Step 11 — Tailscale Serve

Check what is already served before claiming a port — `:8443` on pi5 is **already taken**
(proxying to `127.0.0.1:18080`). `:443` was free as of 2026-08-27:

```bash
tailscale serve status            # confirm :443 is unclaimed first
sudo tailscale serve --bg --https=443 http://localhost:8007
tailscale serve status
```

The client-facing name is the MagicDNS name **`pi5.taile6b761.ts.net`** — **not** `pi5.local`,
which does not resolve from the Mac at all today.

**Funnel is an explicit never.** Do not add `--funnel`; this service has no auth and is gated
entirely at the network layer.

```bash
curl -s https://pi5.taile6b761.ts.net/health   # from another tailnet device
```

---

## Rolling back

```bash
curl -s -X POST http://localhost:8765/stop/task-guide \
  -H "Authorization: Bearer $(cat ~/secrets/dcm_api_key)"
sudo cp /etc/vault-t2/envfiles.yaml.bak /etc/vault-t2/envfiles.yaml
sudo cp /etc/vault-t2/acl.yaml.bak     /etc/vault-t2/acl.yaml
sudo systemctl restart vault-t2-fuse
```

To deregister entirely:

```bash
curl -s -X DELETE http://localhost:8765/registry/task-guide \
  -H "Authorization: Bearer $(cat ~/secrets/dcm_api_key)"
```

`/mnt/data/task-guide` is deliberately left alone — it holds the only copy of anything captured.

---

## Outcome of the 2026-08-27 run

| Step | Result |
|---|---|
| 1 data dir | ✅ `50013:50013 750`, writable by 50013 (via `setpriv`) |
| 2 UID free | ✅ ACL held 50010–50012 only |
| 3 secrets | ✅ both already present — skipped |
| 4 register/deploy | ⚠️ registered and built; stack deploy failed on `env_file` — see step 6 |
| 5 ACL | ✅ purely additive; `dcm secrets check` in sync |
| 6 envfile | ✅ 50013 reads both keys; **root denied**, as designed |
| 7 restart | ✅ n/a — container's first successful start already had credentials |
| 8 data dir intact | ✅ unchanged |
| 9 vertical slice | ✅ 201 + ULID + `Location`; on disk; health `writable: true` |
| 10 **Pushover** | ✅ `POST api.pushover.net → 200`, **notification confirmed on the phone** |
| 11 Tailscale Serve | ✅ `https://pi5.taile6b761.ts.net/` → `:8007`, tailnet only, no Funnel |

Verified from the Mac over the tailnet: `/health`, `/api/tasks` and the SPA all answer over TLS.
`tailscale funnel status` reports both entries as "tailnet only". The pre-existing `:8443` →
`127.0.0.1:18080` mapping was left untouched.

The ownership question below is now settled: a non-root container writing to a host directory
chowned to its UID works on Linux, proven by step 9's 201 rather than a 503.

## Known-unproven going in

- **Ownership of `/mnt/data/task-guide`.** Untestable locally: Docker Desktop on macOS does not
  enforce host UID ownership across its VM file sharing, so a non-root container writing to a
  host-owned directory succeeds on the Mac and proves nothing about Linux. Steps 1 and 7 exist
  for this.
- **Playwright on ARM64/Debian 12** — verified in research, never executed. Not needed here.
- **Backups.** `docs/research/dcm-dotnet-deployment.md` §6 recorded the `/mnt/data` → `/mnt/backup`
  routine as broken; corrected as of 2026-08-26 but **user-reported, not device-verified**. Confirm
  `/mnt/data/task-guide` is actually included before treating captured data as safe, and before
  the Restore drill (#49).
