# Environment Variables Configuration Guide

## Fire-and-Forget Mode Configuration

### SCB_SCANNER__FIREANDFORGET (or SCB_FIRE_AND_FORGET)

**Type:** Boolean  
**Default:** `false` (traditional blocking mode)  
**Required:** No  

Enables fire-and-forget pattern for asynchronous scan execution.

**Accepted Values:**
- `true`, `false` (boolean parsing)
- `1`, `0`, `yes`, `no` (string parsing)
- `True`, `False` (case-insensitive)

**Examples:**

```bash
# Enable fire-and-forget mode
export SCB_SCANNER__FIREANDFORGET=true
export SCB_FIRE_AND_FORGET=1
export SCB_FIRE_AND_FORGET=yes

# Disable (default)
export SCB_SCANNER__FIREANDFORGET=false
```

**Behavior:**

**When `true` (Fire-and-Forget):**
- Scanner launches scan in Invicti
- Returns immediately (no polling)
- Writes metadata-only JSON (empty findings array)
- Exits with code 0
- Invicti webhook triggers GitHub Actions for results collection
- Total execution time: seconds

**When `false` (Traditional Blocking):**
- Scanner launches scan in Invicti
- Polls Invicti for scan completion status
- Waits up to `MaximumWaitMinutes` for scan to finish
- On completion, fetches all issues
- Writes complete findings.json
- Total execution time: scan duration (15m-8h)

---

## Related Environment Variables

### SCB_ACTION

**Type:** String  
**Default:** `scan`  
**Options:** `Scan` or `Issues`  

Determines the primary action the scanner performs.

- `Scan` (or `scan`): Launch a new scan in Invicti
- `Issues` (or `issues`): Fetch issues from existing scan ID

```bash
# Launch new scan
export SCB_ACTION=Scan

# Fetch issues (called by GitHub Actions webhook callback)
export SCB_ACTION=Issues
export SCB_SCAN__ID=550e8400-e29b-41d4-a716-446655440000
```

### SCB_SCANNER__OUTPUTPATH

**Type:** String (file path)  
**Default:** `/home/scanner/results`  

Directory where scanner writes `findings.json` and `scan-metadata.json`.

```bash
export SCB_SCANNER__OUTPUTPATH=/home/scanner/results
```

### SCB_SCAN__ID

**Type:** UUID string  
**Required for:** `SCB_ACTION=Issues`  

Invicti scan ID to fetch results from. Passed by GitHub Actions workflow.

```bash
export SCB_ACTION=Issues
export SCB_SCAN__ID=550e8400-e29b-41d4-a716-446655440000
```

### SCB_INVICTI__URL

**Type:** URL string  
**Required:** Yes  

Base URL of Invicti Enterprise API endpoint.

```bash
export SCB_INVICTI__URL=https://invicti-onprem.your-domain.com
```

### SCB_INVICTI__APIKEY

**Type:** String (API token)  
**Required:** Yes (if not using username/password)  

API key for authenticating with Invicti Enterprise.

```bash
export SCB_INVICTI__APIKEY=your-api-token-here
```

### SCB_SCANNER__POLLINGINTERVALSECONDS

**Type:** Integer (seconds)  
**Default:** `15`  
**Minimum:** `5`  

How frequently to poll Invicti for scan status (traditional blocking mode only).

```bash
export SCB_SCANNER__POLLINGINTERVALSECONDS=15
```

### SCB_SCANNER__MAXIMUMWAITMINUTES

**Type:** Integer (minutes)  
**Default:** `30`  

Maximum time to wait for scan completion (traditional blocking mode only).

```bash
export SCB_SCANNER__MAXIMUMWAITMINUTES=120
```

### SCB_SCAN_DURATION

**Type:** String  
**Options:** `short`, `medium`, `long`, or custom profile name  
**Default:** (inferred from action)  

Selects duration profile controlling scan timeout and severity thresholds.

```bash
# CI/CD quick scan (15m max)
export SCB_SCAN_DURATION=short

# Nightly balanced scan (120m max)
export SCB_SCAN_DURATION=medium

# Full assessment (480m max)
export SCB_SCAN_DURATION=long
```

---

## Complete Example: Fire-and-Forget Setup

### SecureCodeBox ScanType (Helm Values)

```yaml
apiVersion: execution.securecodebox.io/v1
kind: ScanType
metadata:
  name: invicti-enterprise
spec:
  container:
    image: your-registry/invicti-scanner:latest
    imagePullPolicy: IfNotPresent
  
  env:
    # Fire-and-forget configuration
    - name: SCB_ACTION
      value: "Scan"
    - name: SCB_SCANNER__FIREANDFORGET
      value: "true"
    - name: SCB_SCAN_DURATION
      value: "short"
    
    # Invicti connection
    - name: SCB_INVICTI__URL
      valueFrom:
        secretKeyRef:
          name: invicti-credentials
          key: url
    - name: SCB_INVICTI__APIKEY
      valueFrom:
        secretKeyRef:
          name: invicti-credentials
          key: apikey
    
    # Output configuration
    - name: SCB_SCANNER__OUTPUTPATH
      value: "/home/scanner/results"
    
    # Polling (unused in fire-and-forget but good to set)
    - name: SCB_SCANNER__POLLINGINTERVALSECONDS
      value: "15"
```

### GitHub Actions Workflow (fetch-invicti-results.yml)

```yaml
name: Fetch Invicti Scan Results

on:
  workflow_dispatch:
    inputs:
      scan_id:
        required: true
        type: string
      invicti_url:
        required: true
        type: string
      invicti_api_key:
        required: true
        type: string

jobs:
  fetch-results:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      
      - name: Run scanner (Option B)
        env:
          # Option B: Fetch issues only
          SCB_ACTION: Issues
          SCB_SCAN__ID: ${{ inputs.scan_id }}
          
          # Invicti connection
          SCB_INVICTI__URL: ${{ inputs.invicti_url }}
          SCB_INVICTI__APIKEY: ${{ inputs.invicti_api_key }}
          
          # Output
          SCB_SCANNER__OUTPUTPATH: /tmp/results
        run: |
          cd src/InvictiScanner
          dotnet run
      
      - name: Upload to SecureCodeBox
        run: |
          curl -X POST \
            -H "Authorization: Bearer ${{ secrets.SECURECODEBOXAPITOKEN }}" \
            -H "Content-Type: application/json" \
            -d @/tmp/results/findings.json \
            ${{ secrets.SECURECODEBOXAPIENDPOINT }}/v1/findings
```

---

## Troubleshooting

### Scanner Exits But No Results

**Cause:** Fire-and-forget enabled but webhook not configured  
**Solution:** Ensure Invicti webhook is configured to call SecureCodeBox endpoint

### Polling Still Happening

**Cause:** `SCB_SCANNER__FIREANDFORGET` not set to `true`  
**Solution:** Check environment variable is set and spelled correctly

### Wrong Exit Code

| Code | Cause | Solution |
|------|-------|----------|
| `2` | Failed to launch scan | Check Invicti API credentials and connectivity |
| `3` | Scan failed (blocking mode) | Check Invicti logs for scan errors |
| `130` | Operation cancelled | Increase timeout if scans are timing out |

### Empty findings.json in Fire-and-Forget

**Expected behavior:** First run (launch) produces empty findings  
**Normal:** Webhook callback (GitHub Actions) will fetch actual findings  
**Check:** Verify webhook triggered and GitHub Actions workflow completed

---

## Priority Resolution Order

When the same setting can be configured multiple ways, resolution order is:

1. **Command line arguments** (highest priority)
2. **Environment variables** (prefixed with `SCB_`)
3. **appsettings.json** configuration file
4. **Default values** (lowest priority)

Example for `SCB_SCANNER__FIREANDFORGET`:

```csharp
// Priority 1: Command line
--fireAndForget true

// Priority 2: Environment variables
SCB_SCANNER__FIREANDFORGET=true
SCB_FIRE_AND_FORGET=true

// Priority 3: appsettings.json
{
  "Scanner": {
    "FireAndForget": true
  }
}

// Priority 4: Default
false
```

---

## See Also

- `FIRE_AND_FORGET_ARCHITECTURE.md` - Complete architecture documentation
- `appsettings.json` - Default configuration file
- `ScanOrchestrator.cs` - Configuration resolution logic
