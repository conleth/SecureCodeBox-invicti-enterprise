# Visual Reference: Fire-and-Forget Implementation

## Complete Execution Flow Diagram

```
╔══════════════════════════════════════════════════════════════════════════════╗
║                         SECURECODEBOXAPI (SCB)                              ║
║                                                                              ║
║  User triggers scan with ScanType: invicti-enterprise                        ║
║  Annotations:                                                                ║
║    - SCB_ACTION: Scan                                                        ║
║    - SCB_SCANNER__FIREANDFORGET: true                                        ║
║                                                                              ║
╚════════════════════════════════════════╦═════════════════════════════════════╝
                                         │
                                         │ Pods created
                                         ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                        KUBERNETES POD (Scanner)                              │
│                                                                              │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │ ScanOrchestrator.RunAsync()                                            │ │
│  │                                                                        │ │
│  │ 1. Parse environment variables                                       │ │
│  │    ├─ SCB_ACTION = "Scan"                                            │ │
│  │    ├─ SCB_SCANNER__FIREANDFORGET = true ◄─── KEY DECISION POINT      │ │
│  │    └─ Other config (URL, credentials, duration)                      │ │
│  │                                                                        │ │
│  │ 2. ResolveFireAndForgetMode() ──► returns TRUE                        │ │
│  │                                                                        │ │
│  │ 3. action == Scan? ──► YES                                           │ │
│  │                                                                        │ │
│  │ 4. InvictiApiClient.LaunchScanAsync()                                │ │
│  │    └─► POST /api/1.0/scans/scan                                      │ │
│  │        Response: { "id": "550e8400..." }                              │ │
│  │                                                                        │ │
│  │ 5. fireAndForget == true? ──► YES ──► SKIP POLLING ◄─── FAST PATH!  │ │
│  │                                                                        │ │
│  │ 6. metadata.Status = "Launched"                                       │ │
│  │    metadata.ScanId = "550e8400..."                                    │ │
│  │    metadata.Notes = ["Fire-and-forget...", "Webhook...", ...]         │ │
│  │                                                                        │ │
│  │ 7. ScanResultWriter.WriteMetadataOnlyAsync(metadata)                 │ │
│  │    ├─ Write findings.json: []                                        │ │
│  │    └─ Write scan-metadata.json: {scanId, status="Launched", ...}     │ │
│  │                                                                        │ │
│  │ 8. return 0 ──► EXIT IMMEDIATELY ✓                                   │ │
│  │                                                                        │ │
│  │ ⏱️  Total execution time: 2-5 seconds                                 │ │
│  │                                                                        │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │ Mounted Volume: /home/scanner/results/                                │ │
│  │                                                                        │ │
│  │  findings.json ──────────┐                                            │ │
│  │  []                       │                                            │ │
│  │                           │                                            │ │
│  │  scan-metadata.json ──┐   │                                            │ │
│  │  {                    │   │                                            │ │
│  │    "scanId":          │   │                                            │ │
│  │      "550e8400...",   │   │                                            │ │
│  │    "status":          │   │                                            │ │
│  │      "Launched",      │   │                                            │ │
│  │    "mode": "scan",    │   │                                            │ │
│  │    "duration":        │   │                                            │ │
│  │      "ci-fast",       │   │                                            │ │
│  │    "startedAt": "...", │   │                                            │ │
│  │    "notes": [         │   │                                            │ │
│  │      "Fire-and-forget", │   │                                            │ │
│  │      "Webhook callback"│   │                                            │ │
│  │    ]                  │   │                                            │ │
│  │  }                    │   │                                            │ │
│  │                       │   │                                            │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │
                                │ Pod terminates (job complete)
                                │
┌───────────────────────────────▼──────────────────────────────────────────────┐
│                        SECURECODEBOXAPI (SCB)                                │
│                                                                              │
│  Reads metadata.json ──► Status: "Launched"                                 │
│  Mark scan as: In Progress, Awaiting Webhook                                │
│  Pod exits with code: 0 ✓                                                   │
│                                                                              │
│  ⏱️  Total pipeline time: < 10 seconds                                      │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘


░░░░░░░░░░░░░░░░░░░░░░░░ ASYNC PROCESSING (INDEPENDENT) ░░░░░░░░░░░░░░░░░░░░░░░░

┌──────────────────────────────────────────────────────────────────────────────┐
│                      INVICTI ENTERPRISE (ON-PREM)                            │
│                                                                              │
│  Scan 550e8400... is running asynchronously                                  │
│                                                                              │
│  ├─ Launch time: 0:00                                                       │
│  ├─ Current time: 5:00 ──► Scanner already exited                           │
│  ├─ Still running...                                                        │
│  ├─ 5:30 - Still running...                                                 │
│  ├─ 10:00 - Still running...                                                │
│  ├─ ...continues scanning...                                                │
│  │                                                                          │
│  │ Scan Progress (Independent)                                              │
│  │ ████████░░░░░░░░░░░░░░░░░░░░░░░░ 25% complete (5 min elapsed)           │
│  │                                                                          │
│  └─ Eventually...                                                            │
│                                                                              │
│  [20:00] Scan 550e8400... COMPLETED ✓                                       │
│  └─ Found 23 issues                                                         │
│                                                                              │
│  [20:00] Webhook Trigger                                                    │
│  └─► POST https://securecodeboxapi.your-domain/webhooks/                    │
│      invicti-scan-complete                                                  │
│                                                                              │
│      Payload:                                                               │
│      {                                                                      │
│        "scanId": "550e8400...",                                              │
│        "status": "Complete",                                                │
│        "completedAt": "2025-11-18T10:20:00Z",                               │
│        "issueCount": 23                                                     │
│      }                                                                      │
│                                                                              │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │ HTTPS POST
                           ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                        SECURECODEBOXAPI (SCB)                                │
│                                                                              │
│  WebhookController.InvictiScanComplete()                                    │
│  └─ Receives webhook payload                                                │
│  └─ Validates authentication                                                │
│  └─ Extracts scanId: "550e8400..."                                          │
│  └─ Triggers GitHub Actions workflow_dispatch                              │
│                                                                              │
│     Request:                                                                │
│     POST /repos/{owner}/{repo}/actions/workflows/fetch-results/dispatches   │
│     {                                                                       │
│       "inputs": {                                                           │
│         "scan_id": "550e8400...",                                            │
│         "invicti_url": "https://invicti-onprem...",                         │
│         "invicti_api_key": "secret..."                                      │
│       }                                                                     │
│     }                                                                       │
│                                                                              │
└───────────────────────────┬──────────────────────────────────────────────────┘
                            │ GitHub Actions API
                            ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                            GITHUB ACTIONS                                    │
│                                                                              │
│  Workflow: fetch-invicti-results.yml                                        │
│  Trigger: workflow_dispatch (from webhook)                                   │
│                                                                              │
│  Job: fetch-results                                                          │
│  Runner: ubuntu-latest                                                      │
│                                                                              │
│  Steps:                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 1. Checkout scanner code                                           │   │
│  │    └─ git clone https://github.com/.../invicti-scanner            │   │
│  │                                                                    │   │
│  │ 2. Setup .NET 8.0                                                 │   │
│  │    └─ dotnet --version                                            │   │
│  │                                                                    │   │
│  │ 3. Run scanner (OPTION B: Issues)                                │   │
│  │    Environment:                                                   │   │
│  │    ├─ SCB_ACTION = "Issues" ◄─── KEY: Different action!          │   │
│  │    ├─ SCB_SCAN__ID = "550e8400..." ◄─── From webhook             │   │
│  │    ├─ SCB_INVICTI__URL = "https://..." ◄─── From secret          │   │
│  │    ├─ SCB_INVICTI__APIKEY = "..." ◄─── From secret               │   │
│  │    └─ SCB_SCANNER__OUTPUTPATH = "/tmp/results"                   │   │
│  │                                                                    │   │
│  │    Runner execution:                                              │   │
│  │    ┌────────────────────────────────────────────────────────┐    │   │
│  │    │ ScanOrchestrator.RunAsync()                            │    │   │
│  │    │                                                        │    │   │
│  │    │ 1. Parse environment variables                         │    │   │
│  │    │    ├─ action = "Issues"                               │    │   │
│  │    │    └─ SCB_SCAN__ID = "550e8400..."                    │    │   │
│  │    │                                                        │    │   │
│  │    │ 2. if (action == Issues)                              │    │   │
│  │    │    └─ YES! ──► Different path                         │    │   │
│  │    │                                                        │    │   │
│  │    │ 3. InvictiApiClient.FetchIssuesAsync(scanId)          │    │   │
│  │    │    └─► GET /api/1.0/scans/550e8400.../issues          │    │   │
│  │    │        Response: [                                    │    │   │
│  │    │          {                                            │    │   │
│  │    │            "id": "issue-1",                           │    │   │
│  │    │            "title": "SQL Injection",                  │    │   │
│  │    │            "severity": "High",                        │    │   │
│  │    │            ...                                        │    │   │
│  │    │          },                                           │    │   │
│  │    │          ... 22 more issues ...                       │    │   │
│  │    │        ]                                              │    │   │
│  │    │                                                        │    │   │
│  │    │ 4. ScanResultWriter.WriteAsync(issues, metadata)     │    │   │
│  │    │    ├─ findings.json: 23 SecureCodeBoxFinding objects   │    │   │
│  │    │    └─ scan-metadata.json: {status: "Complete", ...}   │    │   │
│  │    │                                                        │    │   │
│  │    │ 5. return 0 ✓                                         │    │   │
│  │    │                                                        │    │   │
│  │    └────────────────────────────────────────────────────────┘    │   │
│  │                                                                    │   │
│  │ 4. Upload findings to SecureCodeBox                             │   │
│  │    Command:                                                      │   │
│  │    curl -X POST \                                                │   │
│  │      -H "Authorization: Bearer {SCB_API_TOKEN}" \                │   │
│  │      -H "Content-Type: application/json" \                       │   │
│  │      -d @/tmp/results/findings.json \                            │   │
│  │      {SCB_API_ENDPOINT}/v1/findings                              │   │
│  │                                                                    │   │
│  │    Response: 201 Created ✓                                        │   │
│  │                                                                    │   │
│  │ 5. Workflow complete                                             │   │
│  │    └─ Job succeeded ✓                                            │   │
│  │                                                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
└──────────────────────────────┬───────────────────────────────────────────────┘
                               │ HTTP POST with findings.json
                               ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                        SECURECODEBOXAPI (SCB)                                │
│                                                                              │
│  POST /v1/findings                                                           │
│  └─ Receive 23 SecureCodeBoxFinding objects                                 │
│  └─ Validate & parse findings                                               │
│  └─ Store in database                                                       │
│  └─ Mark scan as: Complete                                                  │
│  └─ Trigger post-processing:                                                │
│     ├─ Apply filters                                                        │
│     ├─ Apply formatters                                                     │
│     ├─ Generate reports                                                     │
│     └─ Notify subscribers                                                   │
│                                                                              │
│  ✓ SCAN COMPLETE                                                            │
│  ✓ FINDINGS AVAILABLE                                                       │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## Code Path Comparison

### Traditional Blocking Mode (fireAndForget = false)

```csharp
RunAsync()
  │
  └─ Launch scan via InvictiApiClient
      │
      └─ WaitForCompletionAsync()
          │
          ├─ Loop {
          │   ├─ Get scan status via API
          │   ├─ Log "state=InProgress (10/20 steps)"
          │   ├─ Wait 15 seconds
          │   └─ Repeat until Complete|Failed|Cancelled
          │ }
          │
          └─ Return when done
      
      └─ Fetch issues via InvictiApiClient
      │
      └─ WriteAsync() - Write full findings.json
      │
      └─ return 0 or 3

⏱️  TOTAL TIME: 15 min - 8 hours
🔒 BLOCKING: Yes (pod stays running)
💾 RESOURCE: High (container + memory + network)
```

### Fire-and-Forget Mode (fireAndForget = true) ← NEW!

```csharp
RunAsync()
  │
  ├─ ResolveFireAndForgetMode() ──► true ✓
  │
  └─ Launch scan via InvictiApiClient
      │
      ├─ ❌ SKIP WaitForCompletionAsync() ◄─ KEY DIFFERENCE!
      │
      ├─ WriteMetadataOnlyAsync()
      │   ├─ Write empty findings.json: []
      │   └─ Write scan-metadata.json: {scanId, status: "Launched"}
      │
      └─ return 0

⏱️  TOTAL TIME: 2-5 seconds
🔒 BLOCKING: No (exits immediately)
💾 RESOURCE: Minimal (pod exits fast)
```

---

## Environment Variable Resolution Priority

```
ResolveFireAndForgetMode(commandLine)
  │
  ├─ 1st: Check command line arguments
  │   ├─ --fireAndForget true/false
  │   ├─ --fire-and-forget true/false
  │   └─ If found → return that value
  │
  ├─ 2nd: Check environment variables
  │   ├─ SCB_SCANNER__FIREANDFORGET
  │   ├─ SCB_FIRE_AND_FORGET
  │   └─ If found → return that value
  │
  └─ 3rd: Default
      └─ return false (traditional blocking mode)
```

---

## File Output Timeline

### Fire-and-Forget Execution Timeline

```
T=0s    Pod starts
T=1s    Parse configuration, fireAndForget=true
T=2s    Launch scan in Invicti ──► get scan ID 550e8400...
T=3s    Skip polling ──► go straight to WriteMetadataOnlyAsync()
T=4s    Write findings.json: []
T=4.5s  Write scan-metadata.json: {scanId, status: "Launched", ...}
T=5s    return 0 ──► Pod exits ✓

        [Pod is gone, Invicti is still scanning...]

T=20s   [Pod has exited long ago]
        [Invicti is at 50% completion]
        [CI/CD pipeline has already moved on]

T=30s   [Pod still gone]
        [Invicti reaches 80% completion]

T+20m   [Well after original pod exit]
        Invicti scan COMPLETES ✓
        └─ Webhook fires ──► GitHub Actions triggered
        └─ GitHub Actions runs Option B
        └─ Findings uploaded to SCB
        └─ SCB processes findings

SCB Dashboard: COMPLETE ✓
```

### Traditional Blocking Execution Timeline

```
T=0s    Pod starts
T=1s    Parse configuration, fireAndForget=false (default)
T=2s    Launch scan in Invicti ──► get scan ID 550e8400...
T=2-20m Polling loop:
        │ T=17s: Check status ──► "InProgress (5/20)"
        │ T=32s: Check status ──► "InProgress (8/20)"
        │ T=47s: Check status ──► "InProgress (12/20)"
        │ ... continues polling every 15 seconds ...
        │ T=18m: Check status ──► "Complete" ✓

T=18m   Pod still running (hasn't exited)

T=18m15s Fetch issues from completed scan
T=18m20s Write findings.json with all issues
T=18m25s Write scan-metadata.json
T=18m30s return 0 ──► Pod exits ✓

🔒 BLOCKED FOR 18+ MINUTES!
💾 HIGH RESOURCE USAGE!
```

---

## Success vs Failure Scenarios

### Scenario A: Fire-and-Forget Success (Normal Path)

```
✓ Pod exits in seconds
✓ Metadata-only response
✓ Invicti scan runs asynchronously
✓ Webhook fires when complete
✓ GitHub Actions triggered
✓ Results collected via Option B
✓ Findings uploaded to SCB
✓ SCB dashboard shows complete results
```

### Scenario B: Fire-and-Forget with Webhook Delay

```
✓ Pod exits in seconds
✓ Metadata-only response
  [30 minutes pass...]
✓ Invicti scan completes
⏳ Webhook delayed (network issue)
⏳ GitHub Actions waiting for webhook
✓ Webhook eventually fires (+2 minutes)
✓ GitHub Actions triggered
✓ Results collected
✓ SCB updated
```

### Scenario C: Fire-and-Forget with GitHub Actions Failure

```
✓ Pod exits in seconds
✓ Metadata-only response
✓ Invicti scan completes
✓ Webhook fires
✓ GitHub Actions triggered
✗ GitHub Actions workflow fails
  (e.g., Invicti API token expired)
⚠️  Results not in SCB
✓ Admin can manually re-trigger workflow
✓ Results eventually uploaded
```

### Scenario D: Traditional Blocking Mode (Default)

```
✓ Pod stays running
⏳ Polling for 15-120 minutes
  (CI/CD pipeline BLOCKED)
✓ Scan completes
✓ Results fetched
✓ Findings written
✓ Pod exits
✓ SCB receives complete results immediately
🔒 BUT: CI/CD blocked entire time
```

---

## See Also

- `FIRE_AND_FORGET_ARCHITECTURE.md` - Complete architecture with full diagrams
- `QUICK_START_FIRE_AND_FORGET.md` - Quick reference guide
- `ScanOrchestrator.cs` - Source implementation
- `ScanResultWriter.cs` - File writing logic
