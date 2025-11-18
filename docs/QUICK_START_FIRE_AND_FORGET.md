# Quick Start: Fire-and-Forget Pattern

## TL;DR

Enable asynchronous scanning with Invicti Enterprise webhooks in 3 steps:

### Step 1: Enable Fire-and-Forget in SecureCodeBox

```yaml
env:
  - name: SCB_SCANNER__FIREANDFORGET
    value: "true"
```

### Step 2: Configure Invicti Webhook

**Invicti Admin → Settings → Webhooks**

```
URL:      https://securecodeboxapi.your-domain/api/v1/webhooks/invicti-scan-complete
Event:    Scan.Completed
Auth:     Bearer Token (SecureCodeBox API token)
```

### Step 3: Create GitHub Actions Workflow

Save as `.github/workflows/fetch-invicti-results.yml`:

```yaml
name: Fetch Invicti Results
on:
  workflow_dispatch:
    inputs:
      scan_id:
        required: true
        type: string

jobs:
  fetch:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      - run: |
          cd src/InvictiScanner
          env SCB_ACTION=Issues \
              SCB_SCAN__ID=${{ inputs.scan_id }} \
              SCB_INVICTI__URL=${{ secrets.INVICTI_URL }} \
              SCB_INVICTI__APIKEY=${{ secrets.INVICTI_API_KEY }} \
              dotnet run
      - name: Upload findings
        run: |
          curl -X POST \
            -H "Authorization: Bearer ${{ secrets.SCB_API_TOKEN }}" \
            -d @/home/scanner/results/findings.json \
            ${{ secrets.SCB_API_ENDPOINT }}/v1/findings
```

---

## Flow Diagram

```
SCB Trigger
    ↓
Scanner (Option A: Launch)
    ├─→ Call Invicti API
    ├─→ Get scan ID
    ├─→ Write metadata.json (scan ID only)
    └─→ Exit (seconds) ✓
    
[Invicti runs scan async]
    ↓
Invicti completes
    ↓
Invicti webhook fires
    ↓
SCB webhook handler
    ↓
GitHub Actions triggers
    ↓
Scanner (Option B: Issues)
    ├─→ Fetch issues for scan ID
    ├─→ Write findings.json
    └─→ Exit ✓
    ↓
GitHub Actions uploads findings
    ↓
SCB processes results ✓
```

---

## Key Configuration Variables

| Variable | Value | Purpose |
|----------|-------|---------|
| `SCB_SCANNER__FIREANDFORGET` | `true` | Enable fire-and-forget mode |
| `SCB_ACTION` | `Scan` or `Issues` | Which operation to perform |
| `SCB_SCAN__ID` | UUID | Scan ID (only for Issues action) |
| `SCB_INVICTI__URL` | URL | Invicti Enterprise base URL |
| `SCB_INVICTI__APIKEY` | Token | Invicti API authentication |
| `SCB_SCANNER__OUTPUTPATH` | `/home/scanner/results` | Where to write JSON files |

---

## Differences from Traditional Blocking

| Aspect | Traditional | Fire-and-Forget |
|--------|-------------|-----------------|
| **CI/CD Time** | 15m-8h | seconds |
| **Polling** | Yes, continuous | No |
| **Exit on Completion** | Waits for scan | Returns immediately |
| **Result Delivery** | Synchronous | Asynchronous (webhook) |
| **Pod Duration** | 15m-8h | seconds |
| **Resource Usage** | High (blocked pod) | Low (pod exits fast) |

---

## Invicti Webhook Payload

Invicti sends this to SecureCodeBox:

```json
{
  "scanId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Complete",
  "completedAt": "2025-11-18T10:30:00Z",
  "issueCount": 23
}
```

SecureCodeBox extracts `scanId` and passes to GitHub Actions workflow.

---

## Testing

### 1. Verify Launch (Option A)

```bash
# Should exit in seconds with metadata only
SCB_ACTION=Scan SCB_SCANNER__FIREANDFORGET=true dotnet run

# Check metadata.json created
cat /home/scanner/results/scan-metadata.json
# Should show: "status": "Launched"

# Check findings.json is empty
cat /home/scanner/results/findings.json
# Should show: []
```

### 2. Verify Webhook Fires

- Trigger scan in SecureCodeBox
- Check Invicti webhook delivery logs
- Verify POST request sent to SecureCodeBox endpoint

### 3. Verify Results Collection (Option B)

```bash
# Manual test of results collection
SCB_ACTION=Issues \
SCB_SCAN__ID=550e8400-e29b-41d4-a716-446655440000 \
dotnet run

# Check findings.json populated
cat /home/scanner/results/findings.json
# Should show array of SecureCodeBoxFinding objects
```

---

## Common Issues

### Scanner Takes Too Long
- ❌ Fire-and-forget not enabled
- ✅ Set `SCB_SCANNER__FIREANDFORGET=true`

### No Results After Scan Completes
- ❌ Webhook not configured
- ❌ GitHub Actions workflow not exists/enabled
- ✅ Configure Invicti webhook
- ✅ Create GitHub Actions workflow

### Empty Findings
- ✅ Expected for Option A (launch)
- Wait for webhook callback and Option B
- Check GitHub Actions logs if not updating

### GitHub Actions Not Triggering
- ❌ Webhook URL misconfigured
- ❌ Auth token invalid
- ✅ Check Invicti webhook logs
- ✅ Verify SecureCodeBox API token

---

## Exit Codes

```
0   ✓ Success
2   ✗ Failed to launch / invalid config
3   ✗ Scan failed (traditional mode)
130 ✗ Cancelled
```

---

## Documentation Files

- **`FIRE_AND_FORGET_ARCHITECTURE.md`** - Complete technical documentation with full diagrams
- **`CONFIGURATION_GUIDE.md`** - Detailed environment variable reference
- **`ScanOrchestrator.cs`** - Source code with fire-and-forget logic
- **`ScanResultWriter.cs`** - Includes `WriteMetadataOnlyAsync()` method

---

## Next Steps

1. ✓ Read `FIRE_AND_FORGET_ARCHITECTURE.md` for complete understanding
2. ✓ Configure environment variables
3. ✓ Set up Invicti webhook
4. ✓ Create GitHub Actions workflow
5. ✓ Test the complete flow
6. ✓ Monitor logs during first production run
