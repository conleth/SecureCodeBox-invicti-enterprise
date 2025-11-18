# Fire-and-Forget Architecture with Invicti Enterprise Webhooks

## Overview

This document describes the **fire-and-forget pattern** implementation for the Invicti Scanner. This pattern allows the scanner to launch scans asynchronously without blocking CI/CD pipelines, using Invicti Enterprise webhooks for result collection.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        CI/CD PIPELINE                                       │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ SecureCodeBox Scan Orchestrator                                       │ │
│  │                                                                       │ │
│  │ 1. Creates scan job with SCB_SCANNER__FIREANDFORGET=true             │ │
│  │                                                                       │ │
│  └───────────────────────────────────┬─────────────────────────────────┘ │
│                                      │                                   │
│                                      │ HTTP POST                         │
│                                      │ (Launch Scan)                     │
│                                      ▼                                   │
└──────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │
┌─────────────────────────────────────────────────────────────────────────────┐
│                    CONTAINER/SCANNER POD                                   │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ InvictiScanner (Option A: Launch)                                    │ │
│  │                                                                       │ │
│  │ 1. Parse SCB_SCANNER__FIREANDFORGET=true                             │ │
│  │ 2. Call InvictiApiClient.LaunchScanAsync()                           │ │
│  │ 3. Receive scan ID from Invicti                                      │ │
│  │ 4. Skip WaitForCompletionAsync() polling                             │ │
│  │ 5. Write metadata-only (scan ID + status="Launched")                 │ │
│  │ 6. Return immediately (exit code 0)                                  │ │
│  │                                                                       │ │
│  └───────────────────────┬─────────────────────────────────────────────┘ │
│                          │                                               │
│                          │ Write scan-metadata.json                      │
│                          │ (Empty findings.json)                         │
│                          ▼                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ Mounted Volume: /home/scanner/results/                               │ │
│  │  ├── scan-metadata.json  (scan ID, status, timestamps)               │ │
│  │  └── findings.json       (empty array)                               │ │
│  │                                                                       │ │
│  └───────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
└──────────────────────────────┬──────────────────────────────────────────────┘
                               │
                               │ SecureCodeBox reads metadata
                               │ (scan launched, awaiting webhook)
                               ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                    INVICTI ENTERPRISE (ON-PREM)                             │
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ Scan Running Asynchronously                                          │ │
│  │                                                                       │ │
│  │ • API received scan launch request                                   │ │
│  │ • Scan scheduled and running                                         │ │
│  │ • No polling required from scanner                                   │ │
│  │ • Invicti manages the entire scan lifecycle                          │ │
│  │                                                                       │ │
│  └─────────────────────────────────┬─────────────────────────────────────┘ │
│                                    │                                       │
│                                    │ Scan Completes                        │
│                                    │ (status = "Complete")                 │
│                                    ▼                                       │
│  ┌───────────────────────────────────────────────────────────────────────┐ │
│  │ Webhook Trigger (Configured in Invicti Admin)                        │ │
│  │                                                                       │ │
│  │ POST https://securecodeboxapi.your-domain/api/v1/webhooks/           │ │
│  │              invicti-scan-complete                                   │ │
│  │                                                                       │ │
│  │ Payload:                                                              │ │
│  │ {                                                                     │ │
│  │   "scanId": "550e8400-e29b-41d4-a716-446655440000",                 │ │
│  │   "status": "Complete",                                              │ │
│  │   "completedAt": "2025-11-18T10:30:00Z",                             │ │
│  │   "issueCount": 23                                                   │ │
│  │ }                                                                     │ │
│  │                                                                       │ │
│  └───────────────────────────────┬─────────────────────────────────────┘ │
│                                  │                                       │
└──────────────────────────────────┼───────────────────────────────────────┘
                                   │
                                   │ HTTPS
                                   │
┌──────────────────────────────────┼───────────────────────────────────────┐
│                    SECURECODEBOXAPI                                      │
│                                                                          │
│  ┌──────────────────────────────▼──────────────────────────────────────┐ │
│  │ Webhook Endpoint Handler                                           │ │
│  │                                                                    │ │
│  │ 1. Receive webhook from Invicti                                   │ │
│  │ 2. Validate signature/authentication                              │ │
│  │ 3. Extract scan ID                                                │ │
│  │ 4. Trigger GitHub Actions Workflow Dispatch                       │ │
│  │                                                                    │ │
│  └──────────────────────────────┬──────────────────────────────────┘ │
│                                 │                                    │
│                                 │ GitHub Actions API Call            │
│                                 │ workflow_dispatch                  │
│                                 ▼                                    │
└──────────────────────────────────────────────────────────────────────┘
                                  │
                                  │
┌─────────────────────────────────────────────────────────────────────────┐
│                        GITHUB ACTIONS                                  │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────────┐ │
│  │ Workflow: fetch-invicti-results.yml                              │ │
│  │                                                                   │ │
│  │ trigger: workflow_dispatch                                        │ │
│  │ inputs:                                                           │ │
│  │   scan_id: (from webhook)                                        │ │
│  │   invicti_url: (from secrets)                                    │ │
│  │   invicti_api_key: (from secrets)                                │ │
│  │                                                                   │ │
│  └───────────────────────────────┬─────────────────────────────────┘ │
│                                  │                                    │
│  ┌───────────────────────────────▼─────────────────────────────────┐ │
│  │ Step 1: Checkout Scanner Code                                  │ │
│  │ Step 2: Setup .NET 8.0                                         │ │
│  │ Step 3: Run Scanner (Option B: Issues)                         │ │
│  │                                                                 │ │
│  │   env:                                                          │ │
│  │     SCB_ACTION=Issues                                           │ │
│  │     SCB_SCAN__ID=${{ inputs.scan_id }}                          │ │
│  │     SCB_INVICTI__URL=${{ inputs.invicti_url }}                  │ │
│  │     SCB_INVICTI__APIKEY=${{ inputs.invicti_api_key }}           │ │
│  │     SCB_SCANNER__OUTPUTPATH=/tmp/results                        │ │
│  │                                                                 │ │
│  │   dotnet run                                                    │ │
│  │                                                                 │ │
│  └───────────────────────────────┬─────────────────────────────────┘ │
│                                  │                                    │
│  ┌───────────────────────────────▼─────────────────────────────────┐ │
│  │ Step 4: Generate findings from Invicti API                      │ │
│  │ Step 5: Upload findings to SecureCodeBox API                    │ │
│  │                                                                 │ │
│  │   curl -X POST \                                                │ │
│  │     -H "Content-Type: application/json" \                       │ │
│  │     -d @/tmp/results/findings.json \                            │ │
│  │     ${{ secrets.SECURECODEBOXAPIENDPOINT }}/v1/findings         │ │
│  │                                                                 │ │
│  │ Step 6: Workflow completes                                      │ │
│  │                                                                 │ │
│  └───────────────────────────────┬─────────────────────────────────┘ │
│                                  │                                    │
└──────────────────────────────────┼────────────────────────────────────┘
                                   │
                                   │ HTTP POST with findings
                                   │
┌──────────────────────────────────▼────────────────────────────────────┐
│                        SECURECODEBOXAPI                               │
│                                                                        │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ Findings Endpoint: /v1/findings                               │  │
│  │                                                               │  │
│  │ 1. Receive findings from GitHub Actions                      │  │
│  │ 2. Parse and validate SecureCodeBoxFinding[] array           │  │
│  │ 3. Store in database                                         │  │
│  │ 4. Trigger post-processing (filters, formatters, etc.)      │  │
│  │ 5. Mark scan as complete                                     │  │
│  │                                                               │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                        │
│                          ✓ SCAN COMPLETE                              │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

## Execution Flow Summary

### Phase 1: Launch (Synchronous, ~seconds)
1. CI/CD pipeline triggers SecureCodeBox scan with `SCB_SCANNER__FIREANDFORGET=true`
2. Scanner container launches with Option A (Scan action)
3. Scanner calls `InvictiApiClient.LaunchScanAsync()`
4. Invicti returns scan ID immediately
5. Scanner writes metadata-only JSON (includes scan ID)
6. Scanner exits with code 0
7. **Total time: seconds** (no polling)

### Phase 2: Scan Running (Asynchronous, minutes/hours)
1. Invicti Enterprise executes scan in background
2. Scanner pod is gone (no resources consumed)
3. Scan progresses independently in Invicti

### Phase 3: Completion & Results Collection (Asynchronous)
1. Invicti scan completes
2. Invicti webhook fires POST request to SecureCodeBox API
3. SecureCodeBox webhook handler receives notification
4. SecureCodeBox triggers GitHub Actions workflow via `workflow_dispatch`
5. GitHub Actions runner starts fresh job
6. Runner executes scanner with Option B (Issues action)
7. Scanner fetches all issues from Invicti for the scan ID
8. Scanner writes `findings.json` with all issues
9. GitHub Actions uploads findings to SecureCodeBox API
10. SecureCodeBox processes and stores findings

## Configuration

### Environment Variables

```bash
# Fire-and-Forget Mode (Option A: Launch)
SCB_ACTION=Scan
SCB_SCANNER__FIREANDFORGET=true        # Enable fire-and-forget
SCB_INVICTI__URL=https://invicti-onprem.your-domain.com
SCB_INVICTI__APIKEY=your-invicti-api-token
SCB_SCANNER__OUTPUTPATH=/home/scanner/results

# Results Collection (Option B: Issues - run by GitHub Actions)
SCB_ACTION=Issues
SCB_SCAN__ID=550e8400-e29b-41d4-a716-446655440000  # Passed by webhook
SCB_INVICTI__URL=https://invicti-onprem.your-domain.com
SCB_INVICTI__APIKEY=your-invicti-api-token
SCB_SCANNER__OUTPUTPATH=/tmp/results
```

### Invicti Enterprise Webhook Configuration

**Admin Console Path:** Settings → Webhooks (or Administration → Integrations)

**Settings:**
```
Webhook Name:        SecureCodeBox Completion Handler
URL:                 https://securecodeboxapi.your-domain/api/v1/webhooks/invicti-scan-complete
Event Type:          Scan.Completed
HTTP Method:         POST
Authentication Type: Bearer Token
Auth Token:          <SecureCodeBox API Token>
Retry Policy:        Enabled (3-5 attempts)
Timeout:             30 seconds
```

### SecureCodeBox Webhook Configuration

**Helm Values (securecodebox-scanner):**
```yaml
scanType:
  name: invicti-enterprise
  container:
    image: your-registry/invicti-scanner:latest
    imagePullPolicy: IfNotPresent
  
  env:
    - name: SCB_ACTION
      value: "Scan"
    - name: SCB_SCANNER__FIREANDFORGET
      value: "true"
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
  
  webhookUrl: "https://your-github-actions-endpoint/webhook"
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
        description: "Scan ID from Invicti webhook"
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
      - name: Checkout
        uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      
      - name: Run scanner (Option B - Issues only)
        env:
          SCB_ACTION: Issues
          SCB_SCAN__ID: ${{ inputs.scan_id }}
          SCB_INVICTI__URL: ${{ inputs.invicti_url }}
          SCB_INVICTI__APIKEY: ${{ inputs.invicti_api_key }}
          SCB_SCANNER__OUTPUTPATH: /tmp/results
        run: |
          cd src/InvictiScanner
          dotnet run
      
      - name: Upload findings to SecureCodeBox
        run: |
          curl -X POST \
            -H "Content-Type: application/json" \
            -H "Authorization: Bearer ${{ secrets.SECURECODEBOXAPITOKEN }}" \
            -d @/tmp/results/findings.json \
            ${{ secrets.SECURECODEBOXAPIENDPOINT }}/v1/findings
```

## Advantages

| Aspect | Benefit |
|--------|---------|
| **CI/CD Speed** | Returns in seconds, doesn't block pipeline |
| **Resource Efficiency** | Scanner pod exits after launch, no long-running containers |
| **Scalability** | Supports unlimited concurrent scans |
| **Reliability** | Invicti webhook retry logic handles transient failures |
| **Separation of Concerns** | Launch and result collection are decoupled |
| **On-Prem Support** | Works with Invicti Enterprise on-prem webhooks |
| **Audit Trail** | GitHub Actions provides execution history |

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success (scan launched or issues fetched) |
| `2` | Failed to launch scan or invalid configuration |
| `3` | Scan did not complete successfully (traditional blocking mode only) |
| `130` | Operation cancelled |

## Error Handling

### Webhook Failure Recovery
- Invicti retries webhook POST (configurable, typically 3-5 attempts)
- If all retries fail, manual GitHub Actions workflow can be triggered
- SecureCodeBox logs all webhook events for debugging

### GitHub Actions Failure Recovery
- If workflow fails, check GitHub Actions logs for errors
- Common issues:
  - Invalid scan ID format
  - Invicti API credentials expired
  - Insufficient permissions
- Manual re-trigger possible via GitHub Actions UI

### Partial Results
- If webhook fires but GitHub Actions fails, findings won't reach SecureCodeBox
- Scan is marked complete in Invicti but findings missing
- Admin can manually run GitHub Actions workflow to retrieve results

## Comparison: Traditional vs Fire-and-Forget

### Traditional Blocking Mode (Default)
```
SCB_SCANNER__FIREANDFORGET=false

Timeline:
┌─────────────┬──────────────────┬─────────────┐
│ Launch      │ Wait for Scan    │ Fetch       │
│ (instant)   │ (15m-8h)         │ Issues      │
│             │ [BLOCKED]        │ (seconds)   │
└─────────────┴──────────────────┴─────────────┘
Total Time: 15 minutes to 8+ hours
CI/CD Pipeline: BLOCKED entire time
```

### Fire-and-Forget with Webhooks (New)
```
SCB_SCANNER__FIREANDFORGET=true

Timeline:
┌─────────────┬──────────────────────┐       ┌──────────────────┬─────────────┐
│ Launch      │ Exit                 │       │ GitHub Actions   │ Upload      │
│ (instant)   │ (return immediately) │       │ (triggered by    │ Findings    │
│             │ [FREE!]              │  ┄┄┄▶ │  webhook)        │ (seconds)   │
└─────────────┴──────────────────────┘       └──────────────────┴─────────────┘
                Scan Running Asynchronously in Invicti (15m-8h)
Total Time: seconds for CI/CD + async processing
CI/CD Pipeline: UNBLOCKED, continues immediately
```

## Migration Guide

### From Traditional to Fire-and-Forget

1. **Enable fire-and-forget in SecureCodeBox ScanType:**
   ```yaml
   env:
     - name: SCB_SCANNER__FIREANDFORGET
       value: "true"
   ```

2. **Configure Invicti webhook:**
   - Navigate to Invicti Admin Console
   - Add webhook to SecureCodeBox endpoint (must be publicly accessible)
   - Test webhook delivery

3. **Create GitHub Actions workflow:**
   - Copy workflow template from above
   - Add secrets for Invicti and SecureCodeBox credentials
   - Enable workflow_dispatch trigger

4. **Test the flow:**
   - Trigger a scan from SecureCodeBox
   - Verify metadata-only response (exit in seconds)
   - Wait for Invicti to complete scan
   - Verify Invicti webhook fires
   - Check GitHub Actions workflow executed
   - Verify findings uploaded to SecureCodeBox

5. **Update documentation:**
   - Inform team of new async workflow
   - Update CI/CD pipeline expectations
   - Document webhook URL for firewall rules

## Troubleshooting

### Webhook Not Firing
- Check Invicti Admin Console webhook logs
- Verify webhook URL is publicly accessible
- Check SecureCodeBox firewall rules allow Invicti IP
- Verify authentication token is valid

### GitHub Actions Not Triggering
- Check SecureCodeBox webhook handler logs
- Verify GitHub Actions API token has sufficient permissions
- Check GitHub Actions workflow has `workflow_dispatch` trigger
- Verify inputs are passed correctly

### Empty Findings
- Check GitHub Actions workflow logs for Option B execution
- Verify `SCB_ACTION=Issues` is set
- Verify `SCB_SCAN__ID` is passed correctly
- Check Invicti API connectivity in GitHub Actions

### Findings Not in SecureCodeBox
- Check GitHub Actions upload step logs
- Verify SecureCodeBox API endpoint is correct
- Verify API token has write permissions
- Check SecureCodeBox API firewall rules

## See Also

- `ScanOrchestrator.cs` - Main orchestration logic
- `ScanResultWriter.cs` - File writing logic (includes `WriteMetadataOnlyAsync`)
- `InvictiApiClient.cs` - API integration
- `appsettings.json` - Configuration schema
