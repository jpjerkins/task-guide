# First deploy — task-guide to pi5

Walking skeleton (#51), step 6. Run on **pi5** as `philj` unless a step says otherwise.

Nothing here has been executed. Each step ends in a verification with its expected output; if one
does not match, **stop** — the next step assumes it passed.

Modelled on `tto-web-api` in `~/dev/dcm/registry.yaml`, the closest working analogue: a .NET
service on Swarm with a `/data` bind mount and a generated env file.

**Facts this deploy depends on**

| Thing | Value | Why |
|---|---|---|
| Service UID/GID | `50013` | vault-t2 requires one UID per service from 50000–50099, declared in `acl.yaml` |
| Container port | `8007` | `Program.cs` hardcodes `http://0.0.0.0:8007`. **Not 8080** |
| Published port | `8007`, host mode | tailnet-only, TLS terminated by Tailscale Serve |
| Data dir | `/mnt/data/task-guide` → `/data` | explicit bind mount — see step 1 |
| Secrets | `pushover_user`, `pushover_token_task-guide` | vault-t2 Tier 2, read over FUSE |
| Runtime | `swarm` | DCM's default `artifact_format` |

No OS user needs creating. The vault and the kernel both check the UID numerically — hence the
vault docs' `sudo -u '#50013'`.

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
even after the secrets appear. **Step 9 restarts it for exactly this reason — do not skip it.**

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
sudo -u '#50013' touch /mnt/data/task-guide/.probe \
  && sudo -u '#50013' rm /mnt/data/task-guide/.probe \
  && echo "writable by 50013"
```

**Expected:** `writable by 50013`.

---

## Step 2 — Confirm 50013 is free

`50013` was chosen without sight of the live ACL.

```bash
dcm secrets acl-generate | grep -n 50013
sudo grep -n 50013 /etc/vault-t2/acl.yaml
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

Get the repo onto pi5 first — the build runs there, natively on ARM64:

```bash
cd ~/dev && git clone <this-repo> task-guide 2>/dev/null || (cd task-guide && git fetch)
cd ~/dev/task-guide && git checkout walking-skeleton
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
      "read_only": false, "purpose": "data" }
  ],
  "environment": {
    "Storage__DataDir": "/data"
  },
  "env_files": ["/run/vault-t2-fs/envfiles/task-guide"],
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
sudo -u '#50013' cat /run/vault-t2-fs/pushover_user | head -c 4; echo
sudo -u '#50013' cat /run/vault-t2-fs/envfiles/task-guide
```

**Expected:** four characters of the user key, then two lines `Pushover__Token=...` and
`Pushover__UserKey=...` with real values. `Permission denied` means the ACL did not take.

---

## Step 6 — Map the secrets to environment variables

**This is the one step DCM does not own.** There is no `envfiles` handling anywhere in the DCM
source, so this file is edited directly. The app binds `Pushover:Token` / `Pushover:UserKey` from
configuration, and `__` is .NET's nested-key separator, so no code change is needed.

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
docker exec $(docker ps -qf name=task-guide) env | grep -c Pushover__
```

**Expected:** `2`. A `0` means the envfile is not reaching the container — recheck steps 5 and 6.

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
docker exec $(docker ps -qf name=task-guide) env | grep -c Pushover__   # expect 2
```

Being explicit about what this proves: a push landing on your phone is the **first** evidence that
Pushover works at all. The client, the one-push limit and the missing-token no-op were all tested
only against an unconfigured token.

---

## Step 11 — Tailscale Serve

```bash
sudo tailscale serve --bg --https=443 http://localhost:8007
tailscale serve status
```

The client-facing name is the MagicDNS name `pi5.<tailnet>.ts.net` — **not** `pi5.local`, which
does not resolve from the Mac at all today.

**Funnel is an explicit never.** Do not add `--funnel`; this service has no auth and is gated
entirely at the network layer.

```bash
curl -s https://pi5.<tailnet>.ts.net/health    # from another tailnet device
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
