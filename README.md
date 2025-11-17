# SecureCodeBox Invicti Enterprise Scanner

A .NET 8 console application that talks to the Invicti Enterprise REST API (see `invicti.json`) and exposes it as a secureCodeBox compatible scanner. The scanner can launch new scans, resume incremental scans, or just pull issues directly from Invicti while respecting the configurable `SCB_SCAN_DURATION=short|medium|long` mode.

## Project layout

```
.
├── Dockerfile                        # Multi-stage build producing a minimal runtime image
├── invicti.json                      # Official Invicti Enterprise swagger used for reference
└── src/InvictiScanner
    ├── appsettings.json              # Example config (can be overridden by env vars)
    ├── Configuration                 # Strongly typed options, duration profiles
    ├── Models                        # DTOs, enums, SecureCodeBox finding model
    ├── Services                      # API client, orchestration, result writer
    └── Program.cs                    # Host wiring and entrypoint
```

## Configuration

All settings are bound from `appsettings.json` and/or environment variables prefixed with `SCB_`. Nested sections use `__` (double underscore) separators, e.g. `SCB_INVICTI__BASEURL`.

| Setting | Description |
| --- | --- |
| `SCB_INVICTI__BASEURL` | Base URL of your Invicti Enterprise tenant (e.g. `https://invicti.example/api`). Required. |
| `SCB_INVICTI__APITOKEN` / `SCB_INVICTI__APIID` | API token (optionally paired with API ID) to authenticate via `X-Auth`. |
| `SCB_INVICTI__USERNAME` / `SCB_INVICTI__PASSWORD` | Basic-auth fallback when tokens are not available. |
| `SCB_INVICTI__VERIFYTLS` | Set to `false` to skip TLS validation (handy for lab deployments). |
| `SCB_INVICTI__TARGETURI` | Target URL or website root that should be scanned. |
| `SCB_INVICTI__WEBSITENAME` / `SCB_INVICTI__WEBSITEGROUPNAME` | Optional filters used when fetching existing issues. |
| `SCB_INVICTI__SCANPROFILEID` | Invicti scan profile name/ID to force profile-based scans. |
| `SCB_INVICTI__INCREMENTALBASESCANID` | When provided and `SCB_SCAN_DURATION=short`, incremental scans are launched from this scan ID. |
| `SCB_SCANNER__OUTPUTPATH` | Location where `findings.json` & `scan-metadata.json` are written. Default: `/home/scanner/results`. |
| `SCB_SCANNER__POLLINGINTERVALSECONDS` | Poll interval while waiting for scan completion (default 15s). |
| `SCB_SCANNER__MAXIMUMWAITMINUTES` | Global wait budget for a scan before timing out. |
| `SCB_ACTION` | `scan` (default) or `issues`. Use `issues` to skip launching scans and only pull outstanding issues. |
| `SCB_FETCH_ISSUES_ONLY` | Boolean alias for `SCB_ACTION=issues`. |
| `SCB_SCAN_DURATION` | `short` (default), `medium`, or `long`. Controls runtime budget & severity filters. |

Custom duration overrides can also be supplied via `SCB_DURATION__SHORT__MAXRUNTIMEMINUTES`, `SCB_DURATION__MEDIUM__MAXISSUES`, etc.

## Duration profiles

| Duration | Max runtime | Severity floor | Typical usage |
| --- | --- | --- | --- |
| `short` | 15 minutes (1 hour cap enforced by Invicti API) | `High` | CI/CD smoke scans hitting incremental base job and only fetching critical/high findings. |
| `medium` | 120 minutes | `Medium` | Daily sanity scans with balanced coverage. |
| `long` | 480 minutes | `BestPractice` | Full assessment with the lowest severity threshold and unrestricted crawl. |

> **Note:** Invicti exposes `MaxScanDuration` in hours. Sub-hour budgets are rounded up to 1 hour, but the scanner still terminates polling based on the minute values above.

## Running locally

1. Install the .NET 8 SDK (not available on this runner, so restore/build has not been executed yet).
2. Populate environment variables (see table above) or edit `src/InvictiScanner/appsettings.json`.
3. From the repo root run:
   ```bash
   dotnet run --project src/InvictiScanner/InvictiScanner.csproj -- --duration short
   ```
4. Results appear in `SCB_SCANNER__OUTPUTPATH` (`findings.json` + `scan-metadata.json`).

Supported CLI switches:

- `--duration short|medium|long`
- `--action scan|issues`
- `--issues true` (shorthand for `--action issues`)

## Docker image

Build and run the scanner as a container:

```bash
docker build -t invicti-scanner .
docker run --rm \
  -e SCB_INVICTI__BASEURL=https://invicti.example/api \
  -e SCB_INVICTI__APITOKEN=xxxx \
  -e SCB_INVICTI__TARGETURI=https://app.example \
  -e SCB_SCAN_DURATION=short \
  -v $(pwd)/results:/home/scanner/results \
  invicti-scanner
```

The Docker image runs as the non-root `scb` user and writes results under `/home/scanner/results` (exported as a volume for secureCodeBox). The swagger file is copied into `/app/swagger/invicti.json` for debugging or custom tooling.

## secureCodeBox integration

Register the container as a custom scanner (`scanType`) and mount the standard result volume:

```yaml
apiVersion: "execution.securecodebox.io/v1"
kind: Scan
metadata:
  name: invicti-ci
spec:
  scanType: invicti-enterprise
  persistentVolumeClaim: "scb-results"
  env:
    - name: SCB_INVICTI__BASEURL
      valueFrom:
        secretKeyRef:
          name: invicti-credentials
          key: base-url
    - name: SCB_INVICTI__APITOKEN
      valueFrom:
        secretKeyRef:
          name: invicti-credentials
          key: api-token
    - name: SCB_INVICTI__TARGETURI
      value: "https://app.example"
    - name: SCB_SCAN_DURATION
      value: "short" # accept medium or long when engineers need deeper coverage
```

- **CI/CD feedback**: keep `SCB_SCAN_DURATION=short` and set `SCB_INVICTI__INCREMENTALBASESCANID` to an approved baseline scan.
- **Nightly / weekend scans**: use `SCB_SCAN_DURATION=medium` and optionally point to an extended profile with `SCB_INVICTI__SCANPROFILEID`.
- **Deep dive**: set `SCB_SCAN_DURATION=long` explicitly in the Scan custom resource when developers request it.
- Use `SCB_ACTION=issues` to sync Invicti issues into secureCodeBox without launching a new scan (e.g., scheduled compliance jobs).

The container already emits secureCodeBox-formatted `findings.json`, so you can reuse the generic `noop-parser` or wire a custom parser if you need additional transformations.

## API reference

- Refer to `invicti.json` for the complete swagger definition (paths like `/api/1.0/scans/new`, `/api/1.0/scans/status/{id}`, `/api/1.0/issues`).
- `InvictiApiClient` currently uses `X-Auth` headers for token-based access and falls back to basic authentication when a username/password pair is supplied.

## Troubleshooting

- **dotnet: command not found** – install the .NET 8 SDK/Runtime locally or rely on the Docker image. This environment did not have `dotnet`, so commands such as `dotnet restore` were not executed yet.
- **TLS validation issues** – set `SCB_INVICTI__VERIFYTLS=false` for lab environments.
- **No findings written** – ensure `SCB_SCANNER__OUTPUTPATH` is writable inside the container/pod. The Docker image already exposes `/home/scanner/results` as a volume.
