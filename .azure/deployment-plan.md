# Hermes C# Trip Deployment

> **Status:** Deployed and verified

## Debug UI application update (2026-11-04)

User requested deploying the debug-enabled web application to the same group,
subscription and private network. Application-only replacement; no Bicep,
Storage, DNS, identity or RBAC changes. Existing instance is already stopped
by the user. Preserve it as stopped rollback until cleanup is approved.
Validation steps: tests, publish, Docker configuration check, current Azure
authentication/group inventory, unchanged role review, then real request and
authenticated debug-event retrieval on the replacement.

Validation proof: 16 tests passed; Release publish and Docker configuration
check passed. Image
`sha256:3051dd232fc0fc798ac91d08cb3e76a759aa5d7d9065220965783f25b4349f0b`.
Azure account confirms JJAzDev Internal1 and expected subscription. Live
inventory confirms old instance stopped. Deployment uses existing group,
network and unchanged Storage data roles; no infrastructure deployment needed.

Deployment evidence:
- New image `28ac6390-e80c-4809-a3e0-813827f70406`, Ready.
- New sandbox `3bad566b-e007-43b0-9522-8d2d44a0d128`, public web:
  `https://3bad566b-e007-43b0-9522-8d2d44a0d128--8080.swedencentral.adcproxy.io`.
- Health, debug HTML, static assets, authenticated old result recovery and
  unauthenticated rejection passed. Existing result retained 4,287 characters.
- Worker first processed previously queued job
  `4ed1ff79-27d9-492a-b0ac-b7b157066e17`, then smoke job
  `7e81e126-7ca1-413a-9f17-d70c9f9cfb98`. Both have persistent real debug
  events, including delegation start and terminal failure. Both timed out.
- Sanitized runtime log classification found 24 HTTP 429 and 42
  RateLimitError occurrences. Successful new itinerary validation is blocked
  by model provider rate limiting; do not describe it as passed.
- Original sandbox remains stopped for rollback; no network/data deletion.
- Live RBAC query confirms unchanged Blob/Queue/Table Data Contributor roles.

Successful full retest (2026-11-04, 18:00:39Z):
16 C# tests, JavaScript syntax and diff checks passed. Live job
`80f0d7fb-53bb-4f22-8e71-ca3a913ed5f6` completed with 4,238 characters.
Form/health, authenticated API and unauthenticated rejection passed.
Separate authenticated read confirmed nine persistent debug events, including
real delegation running/completed and result Blob persistence.
This successful retest supersedes the earlier rate-limit validation blocker;
model rate limiting can still delay subsequent requests.

## Azure rename execution requested

User explicitly requested deploying the Trip naming in Azure and removing
the superseded resources. Inventory exact app-scoped resources first.
Create renamed private endpoints and Trip image/sandbox, keep existing data,
validate old result recovery and a real trip before deleting old instance,
old trip-intake endpoints and obsolete trip-intake disk images.
Preserve shared VNet, sandbox group, Storage account, identity, data and
unrelated coding sandbox. Confirm concrete deletion scope after inventory.

Inventory and implementation plan: retain subscription
`80214fc0-7aff-43b6-bbff-cf81a36cbb10`, `swedencentral`, group `jjsandbox-gr1`.
Deploy renamed Bicep into `rg-jjsandbox` (three `trip-storage-*` endpoints);
preserve the VNet and DNS zone names. Import renamed validated image as
`hermes-trip`, deploy with `Trip.dll`, `TRIP_API_KEY` and app=hermes-trip.
Stop old worker before submitting new work, recover the previous completed
result, complete a new real trip, then delete old sandbox
`2740e67c-c6f3-40ed-82af-e08478df3bfc`, six old trip-intake endpoints in
`rg-jjsandbox` and `rg-jjnetwork`, six old trip-intake image IDs from the
live inventory, and `jjvnethub-trip-intake` connection. No storage data deletion.
User explicitly requested this Azure replacement and old-resource cleanup.

Azure rename validation checklist:
- [x] Official core helper: CLI/auth/Bicep/ARM validate/what-if OVERALL PASS,
  2026-11-04. ResourceIdOnly confirms six new endpoint/DNS-group resources;
  existing old endpoints are Ignore and data account unchanged. Text summary
  property deltas are not resource deletions.
- [x] Renamed image build and 15 tests pass (previous turn).
- [x] Policy and least-privilege role review; no RBAC changes, same private
  endpoint settings as previous approved deployment, ARM checks succeed.
- [x] Private DNS and old/new result retrieval after old endpoint deletion.
- [x] One active renamed sandbox, no old trip-intake resources/images.

Updated: 2026-11-04

## Azure rename execution evidence (2026-11-04)

- Bicep deployment `trip-network`: Succeeded; endpoints `trip-storage-blob`,
  `trip-storage-queue`, `trip-storage-table`, all Approved.
- Image `hermes-trip`: `3c709063-4eca-4644-b885-546e2e51f6d1`, Ready.
- New sandbox `c2a33bf1-fb60-4241-b754-9d74b5ac60c1`, Running, app=hermes-trip,
  `Trip.dll`, `TRIP_API_KEY`, VNet connection `jjsandbox-trip`.
- Endpoint:
  `https://c2a33bf1-fb60-4241-b754-9d74b5ac60c1--8080.swedencentral.adcproxy.io`.
- Previous result `ee98b0c2-f33c-4a65-9d6e-5119c66419a6` recovered:
  completed, 4,883 characters at 14:54:29Z.
- Old worker stopped before submission. Real new job
  `27a5710f-6213-4eed-832f-f7fb016de20d`: completed, 4,736 characters at
  14:55:27Z. Health/form and unauthorized rejection passed.
- Deleted old sandbox `2740e67c-c6f3-40ed-82af-e08478df3bfc`, all six
  superseded image IDs listed in inventory and `jjvnethub-trip-intake`.
- Deleted six old `trip-intake-storage-{blob,queue,table}` endpoint resources
  across `rg-jjsandbox` and `rg-jjnetwork`; associated old NICs disappeared.
- Deleting old private endpoints removed shared DNS A records despite new
  zone groups. Reapplying `dns-zone-group create` succeeded but did not restore
  the missing records. Repaired each A record using its actual new endpoint
  NIC address: Blob `10.42.2.9`, Queue `10.42.2.7`, Table `10.42.2.8`.
- After all deletions, `getent hosts` inside the new sandbox resolves all
  three service FQDNs to those private addresses. New result retrieval passed
  at 15:02:19Z. Post-cleanup fresh job
  `fe0467e7-ea77-46f2-9d97-e7e57a922b73` completed with 4,287 characters
  at 15:03:30Z; authenticated API, form and unauthorized rejection passed.
- Final live inventory: exactly one Running sandbox and one Ready disk image,
  both hermes-trip; subscription ARM resource inventory contains no trip-intake
  names. Storage public network remains Disabled, shared keys and public Blob
  access remain false. No secret files tracked; `git diff --check` passes.
- Live RBAC query confirms Queue/Table/Blob Data Contributor for unchanged
  group identity at Storage account scope.

The following sections retain historical evidence from before Azure cutover;
live identifiers and endpoint above supersede historical instance details.

## Repository rename and secret relocation

The user requested replacing trip-intake naming with trip and moving local
configuration from LocalAppData into ignored repository folder `config-secret`.
Solution is now `Trip.slnx`, projects `src/Trip/Trip.csproj` and
`test/Trip.Tests/Trip.Tests.csproj`, namespaces `Trip` and `Trip.Tests`.
Docker and deployment entrypoint use `Trip.dll`; the static-key setting is
`TRIP_API_KEY`. Infrastructure and smoke-test filenames use `trip`.

Configuration was moved to `config-secret/runtime.json` with owner-only
Windows ACLs, values verified in memory, and the original LocalAppData file
and empty directory removed. Git and Docker exclude `config-secret`.
No configuration values were printed, staged or added to the image.

The repository rename initially retained the prior live instance. The Azure
replacement recorded above now supersedes it; historical deployment evidence
below predates that replacement. Endpoint renaming required new resources.
The earlier temporary-manifest design was superseded by in-memory API creation.

Rename verification: all 15 tests pass after deleting obsolete build outputs.
Fresh `Trip.dll` publish and Docker configuration check pass; renamed image
`sha256:4271b53f3f500927e12b231204ff47e96c6f522ad5e4deb003f6874503608ba6`.
The renamed smoke script uses the relocated configuration successfully to
recover completed job `ee98b0c2-f33c-4a65-9d6e-5119c66419a6`.
Git ignore and restricted ACL checks pass; no secret directory files are tracked.

## 1. Scope and decisions

User requested cleanup, a new VNet in the sandbox resource group, obsolete
sandbox deletion, C# replacement of custom Python, static-key deployment
configuration, tests and a deployment/runtime explanation.
Subsequent explicit requests removed root npm files, `node_modules`, `public`
after moving assets into the C# project, and the unused OpenClaw demo.
The user instructed autonomous implementation.

Preserve upstream Hermes: its Python engine provides the real travel-demo
orchestration. All application code, supervision and config checking are C#.
PowerShell handles deployment and tests; no custom Python snippets remain.
README explains the boundary and contains the requested Mermaid architecture.

## 2. Target and recipe

- Recipe: Azure CLI/Bicep for networking, ACA Sandbox CLI for disk import and
  lifecycle, authenticated Sandbox data-plane API for secret-safe creation.
  This is not a regular Container App or an azd project.
- Subscription: JJAzDev Internal1 / `80214fc0-7aff-43b6-bbff-cf81a36cbb10`.
- Region: `swedencentral`; resource group: `rg-jjsandbox`.
- Existing group: `jjsandbox-gr1`, identity
  `9a74de07-d6a4-4366-bd87-d43963b625f8`.
- Storage: `tripfiv6psfk5vqje`; existing Queue/Table `triprequests`,
  Blob container `trip-results`.
- Final disk: `ab47c1cd-dac8-496e-b5ec-585d55b8d2e3`, Ready.
- Final Docker image:
  `sha256:4b16d9d865086871fab76bd1bc382ca3052cb4b77afdfe22dbc9cd67002b6d94`.
- Running sandbox: `2740e67c-c6f3-40ed-82af-e08478df3bfc`, 1 vCPU / 2 GiB.
- Endpoint:
  `https://2740e67c-c6f3-40ed-82af-e08478df3bfc--8080.swedencentral.adcproxy.io`.

## 3. Network

Dedicated `vnet-jjsandbox-trip` is deployed in `rg-jjsandbox`.
Address space `10.42.0.0/16`, ACA delegated subnet `10.42.0.0/23`,
private endpoint subnet `10.42.2.0/27`.
New group connection `jjsandbox-trip` is Succeeded and the final sandbox
explicitly uses it.

Three private endpoints and private DNS zones/links are in `rg-jjsandbox`;
all endpoint connections Approved. Final sandbox resolves Blob to
`10.42.2.6`, Queue to `10.42.2.4`, Table to `10.42.2.5`.
Existing Storage public networking remains Disabled, shared key and anonymous
Blob access remain false. No policy exemptions or RBAC changes.
The old shared `jjvnethub`, connection and network resources in `rg-jjnetwork`
are preserved; the new deployment does not depend on them.

## 4. Application and credentials

Browser -> C# authenticated API -> Azure Queue -> C# worker -> C# SSE client
-> loopback Python Hermes Gateway -> Program coordinator -> one batch of
Route planner, Meteorologist and Budget analyst -> final itinerary.
Table stores progress; private Blob stores the result.
Gateway port 8642 is loopback-only; only intake 8080 is exposed by HTTPS proxy.

`TRIP_API_KEY` is a static deployment environment setting, at least
32 characters. It is not in HTML, JavaScript, URL, source or image.
Deployment reads protected local runtime JSON and sends the key and distinct
Copilot credential only in memory through HTTPS request content.
No temporary credential file or secret CLI argument is required.
C# generates the internal Gateway key on startup.

The preview service mutates/reserves `COPILOT_GITHUB_TOKEN` environment
configuration; passing it directly resulted in a 15-character placeholder.
Use `TRIP_COPILOT_TOKEN` instead, then explicitly set the Hermes child process
`COPILOT_GITHUB_TOKEN` in C#. The final instance receives the real credential.
Never snapshot or commit a credential-bearing sandbox.

## 5. Deployment implementation

1. `dotnet test Trip.slnx`.
2. `infra/build-trip-image.ps1`: host publish, Docker build, C# `--check-config`.
3. ARM validate/what-if and group deployment of dedicated networking.
4. Create `jjsandbox-trip` group VNet connection.
5. ACA disk import of published application and Dockerfile.
6. `infra/deploy-trip-sandbox.ps1 -DiskId ...`.
7. Check internal health and private Storage, expose 8080, run public trip test.

ACA manifest apply created holding sandboxes rather than running the intended
entrypoint in this preview. The final helper uses the observed native create
contract: PUT sandbox collection, API `2026-02-01-preview`, explicit entrypoint,
empty cmd, environment, disk reference and VNet connection. Authentication
scope is `https://dynamicsessions.io/.default`, confirmed by ACA auth status
and authenticated GET. POST incorrectly returned 401; no blind retries after
unknown create outcomes were used.

## 6. Cleanup

Removed root `node_modules`, npm manifests, unused OpenClaw source and empty
directories. Web is now `src/Trip/wwwroot`; tests confirm HTML and JS
are served. Removed custom Python config-transfer script and build snippets;
C# checks model context and delegation settings.
Generated Bicep JSON is excluded; Bicep and bicepparam are source of truth.

After persisted live success, removed these obsolete trip instances:
- `0eb121f1-18e8-48f3-b1cf-4deb1134265a`
- `f145b230-66a5-44ca-b6f6-59745386ed92`
- `795d3f22-694e-4058-870b-2e4e9f3abbc7`
- `5e74459a-54b2-46f3-b6af-792dcea08f07`
- `0c64f6bf-442b-41e8-894f-a2341c9904c1`
- `fdddbbb3-1b86-4aa6-a1d4-3e98476c6af3`
- `caf0736b-d92a-42c0-9f83-aa821c3de587`
- `dfbd6140-759a-4507-b678-54f3152e284d`
- `4eada456-cd19-431d-be59-dbd25bbcc3bb`

Final listing: only final trip sandbox Running; unrelated coding sandbox
`2e2a0d8d-cd9b-4db1-91f5-98d073665277` remains Stopped and was not modified.
Storage results are preserved; deleted sandbox-local histories are not.

## 7. Validation Proof

All validation checks pass:
- [x] Official azure-validate workflow completed through UpdateStatus.
- [x] Official validate-deployment.ps1: CLI, authentication, Bicep compilation,
  ARM validation and what-if OVERALL PASS; 14 creates, 0 modifies, 0 deletes.
- [x] Network capacity: 3/1000 VNets, 3/65536 endpoints before adding 1 and 3.
- [x] Azure MCP inherited policies reviewed; no ARM policy rejection or bypass.
- [x] Static and live RBAC: Queue, Table, Blob Data Contributor at Storage
  account scope for group managed identity; unchanged.
- [x] 15 C# tests pass: configuration, API auth/JSON/recovery, worker states,
  SSE success/error/incomplete/truncation, shared travel-demo prompt.
- [x] Docker build and C# configuration verification pass: context exactly
  64000, delegation depth 1, three leaf specialists.
- [x] Final live private endpoints Succeeded/Approved; private DNS confirmed.
- [x] Nonroot .NET and Hermes processes both present, internal/public health OK.
- [x] Public form, static JS, authenticated API and unauthenticated 401.
- [x] Actual trip `ee98b0c2-f33c-4a65-9d6e-5119c66419a6` completed:
  created 14:33:16 UTC, completed 14:42:18 UTC, result 4,883 characters,
  error null. Authenticated persisted recovery rechecked at 14:42:57 UTC.
- [x] Hermes session `api-5763e64a2bbbec4d`: three leaves and root each
  ended with finish_reason stop and two model calls; delegation completed
  in 180.03 seconds, root response ready in 540.8 seconds. No error in run.
- [x] Only public port 8080; only one running trip sandbox after cleanup.

Initial connection-refused messages occurred while the Gateway initialized;
readiness retry then returned healthy before job submission.
Hermes startup warnings about plugin migration, Copilot raw-token fallback
and intentionally pinned context did not prevent successful processing.
Completion is an execution guarantee, not a guarantee of generated travel facts.
