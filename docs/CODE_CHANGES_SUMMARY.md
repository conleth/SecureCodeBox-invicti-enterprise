# Code Changes Summary

## Overview

This document summarizes all code changes made to implement the fire-and-forget pattern with Invicti Enterprise webhooks.

---

## 1. ScanOrchestrator.cs

### Added Method: `ResolveFireAndForgetMode()`

```csharp
/// <summary>
/// Resolves whether fire-and-forget mode should be enabled.
/// 
/// Fire-and-forget mode allows the scanner to launch a scan in Invicti Enterprise
/// and return immediately without waiting for completion. The scan continues
/// asynchronously, and Invicti's webhook notifies SecureCodeBox when it completes.
/// SecureCodeBox then calls back with Option B (Issues) to collect results.
/// 
/// Environment variables (in priority order):
/// - SCB_SCANNER__FIREANDFORGET: "true"|"false"|"1"|"0"|"yes"|"no"
/// - SCB_FIRE_AND_FORGET: Same options
/// 
/// Command line argument: --fireAndForget or --fire-and-forget
/// </summary>
private bool ResolveFireAndForgetMode(CommandLineArguments arguments)
{
    // Check command line arguments first
    var fireAndForgetArg = arguments["fireAndForget"] ?? arguments["fire-and-forget"];
    if (!string.IsNullOrWhiteSpace(fireAndForgetArg))
    {
        if (bool.TryParse(fireAndForgetArg, out var result))
        {
            return result;
        }
        if (string.Equals(fireAndForgetArg, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fireAndForgetArg, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    // Check environment variables
    var envValue = Environment.GetEnvironmentVariable("SCB_SCANNER__FIREANDFORGET") ?? 
                  Environment.GetEnvironmentVariable("SCB_FIRE_AND_FORGET");
    
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        if (bool.TryParse(envValue, out var result))
        {
            return result;
        }
        if (string.Equals(envValue, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(envValue, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    // Default to false (traditional blocking mode)
    return false;
}
```

### Modified Method: `RunAsync()`

**Key Changes:**
1. Added fire-and-forget mode detection
2. For Option A (Scan), check fire-and-forget setting
3. If fire-and-forget enabled:
   - Launch scan
   - Call `WriteMetadataOnlyAsync()` instead of polling
   - Return immediately with exit code 0
4. If fire-and-forget disabled:
   - Use original blocking behavior (poll and wait)
5. For Option B (Issues), behavior unchanged

**Execution paths:**

```
RunAsync()
├─ Option B (Issues)
│  └─ Fetch and write issues (unchanged)
│
└─ Option A (Scan)
   ├─ Launch scan
   │
   ├─ If fireAndForget = true
   │  ├─ Write metadata-only
   │  └─ Return 0 (FAST PATH)
   │
   └─ If fireAndForget = false (default)
      ├─ Poll for completion
      ├─ Fetch issues
      ├─ Write full findings
      └─ Return 0 or 3 (BLOCKING PATH)
```

---

## 2. ScanResultWriter.cs

### Added Method: `WriteMetadataOnlyAsync()`

```csharp
/// <summary>
/// Writes scan metadata only (without findings) for fire-and-forget mode.
/// 
/// This method is used when the scanner launches a scan and returns immediately
/// without waiting for completion. Only metadata is written, which includes:
/// - Scan ID for webhook callback
/// - Status ("Launched")
/// - Timestamps and target information
/// - Notes explaining the fire-and-forget workflow
/// 
/// The actual findings will be written later when SecureCodeBox webhook
/// triggers Option B (Issues fetch) after Invicti completes the scan.
/// </summary>
public async Task WriteMetadataOnlyAsync(ScanMetadata metadata, CancellationToken cancellationToken)
{
    Directory.CreateDirectory(_settings.OutputPath);
    var metadataPath = Path.Combine(_settings.OutputPath, "scan-metadata.json");
    
    // Create empty findings array for consistency with schema
    var emptyFindings = Array.Empty<SecureCodeBoxFinding>();
    var findingsPath = Path.Combine(_settings.OutputPath, "findings.json");

    // Write empty findings
    await using (var findingsStream = File.Create(findingsPath))
    {
        await JsonSerializer.SerializeAsync(findingsStream, emptyFindings, SerializerOptions, cancellationToken);
    }

    // Write metadata with detailed notes about the fire-and-forget mode
    await using (var metadataStream = File.Create(metadataPath))
    {
        await JsonSerializer.SerializeAsync(metadataStream, metadata, SerializerOptions, cancellationToken);
    }

    _logger.LogInformation(
        "Wrote metadata-only result for scan {ScanId} in fire-and-forget mode to {Path}. " +
        "Awaiting Invicti webhook callback to fetch final results.", 
        metadata.ScanId, metadataPath);
}
```

**Output Files Created:**
- `findings.json`: Empty array `[]`
- `scan-metadata.json`: Contains scan ID, timestamps, status = "Launched"

---

## 3. Configuration (appsettings.json)

No changes required - configuration is loaded via environment variables:

```bash
# Fire-and-forget mode (new)
SCB_SCANNER__FIREANDFORGET=true

# Existing variables (still supported)
SCB_ACTION=Scan
SCB_SCAN_DURATION=short
SCB_INVICTI__URL=https://...
SCB_INVICTI__APIKEY=...
SCB_SCANNER__OUTPUTPATH=/home/scanner/results
```

---

## 4. Backwards Compatibility

✅ **Fully backwards compatible:**

- Default behavior unchanged (`fireAndForget = false`)
- Existing deployments continue working
- No breaking changes to APIs
- All existing environment variables still work
- New feature is opt-in

**Behavior:**
```
SCB_SCANNER__FIREANDFORGET not set or false
    → Uses original blocking behavior (polls and waits)
    
SCB_SCANNER__FIREANDFORGET=true
    → Uses new fire-and-forget behavior (returns immediately)
```

---

## 5. File Output Changes

### Traditional Blocking Mode (unchanged)

```json
// findings.json
[
  {
    "name": "SQL Injection",
    "category": "Invicti Issue",
    "severity": "high",
    ...
  },
  ...
]

// scan-metadata.json
{
  "scanId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Complete",
  "startedAt": "2025-11-18T10:00:00Z",
  "completedAt": "2025-11-18T10:30:00Z",
  ...
}
```

### Fire-and-Forget Mode (new)

**First execution (Option A: Launch)**
```json
// findings.json (empty)
[]

// scan-metadata.json
{
  "scanId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Launched",
  "startedAt": "2025-11-18T10:00:00Z",
  "completedAt": "2025-11-18T10:00:05Z",
  "notes": [
    "Quick CI/CD coverage",
    "Fire-and-forget mode enabled. Scan is running asynchronously.",
    "SecureCodeBox will receive completion webhook from Invicti Enterprise.",
    "Webhook will trigger Option B (Issues fetch) to collect results."
  ]
}
```

**Second execution (Option B: Issues - triggered by webhook)**
```json
// findings.json (populated)
[
  {
    "name": "SQL Injection",
    "category": "Invicti Issue",
    "severity": "high",
    ...
  },
  ...
]

// scan-metadata.json (updated)
{
  "scanId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Complete",
  "startedAt": "2025-11-18T10:00:00Z",
  "completedAt": "2025-11-18T10:30:00Z",
  ...
}
```

---

## 6. Exit Codes (unchanged)

| Code | Scenario |
|------|----------|
| `0` | Success (launch or issue fetch completed) |
| `2` | Failed to launch scan or invalid config |
| `3` | Scan failed (traditional blocking mode only) |
| `130` | Operation cancelled |

---

## 7. Logging

### Fire-and-Forget Mode

```
[INF] Scanner starting with action=Scan, duration=ci-fast, fireAndForget=true
[INF] Scan launched in fire-and-forget mode. Scan metadata written.
[INF] Scan 550e8400-e29b-41d4-a716-446655440000 launched in fire-and-forget mode...
```

### Traditional Blocking Mode

```
[INF] Scanner starting with action=Scan, duration=ci-fast, fireAndForget=false
[INF] Waiting up to 00:15:00 for scan 550e8400-e29b-41d4-a716-446655440000 completion
[INF] Scan state=InProgress (10/20 steps)
[INF] Scan state=Complete
[INF] Scan completed with status Complete. 23 issues written.
```

---

## 8. Testing Checklist

- [ ] Fire-and-forget launch returns in < 5 seconds
- [ ] Metadata-only JSON written correctly
- [ ] Findings.json is empty array
- [ ] Scan ID captured in metadata
- [ ] Exit code 0 on success
- [ ] Blocking mode still works (backwards compat)
- [ ] Polling logic unchanged for blocking mode
- [ ] Environment variable resolution works
- [ ] Command line arguments override env vars
- [ ] GitHub Actions workflow receives scan ID
- [ ] Option B (Issues) fetches findings correctly
- [ ] Full findings.json written by Option B
- [ ] SecureCodeBox reads findings successfully

---

## 9. Code Files Modified

1. **`src/InvictiScanner/Services/ScanOrchestrator.cs`**
   - Added `ResolveFireAndForgetMode()` method
   - Modified `RunAsync()` to check fire-and-forget setting
   - Added fire-and-forget execution path

2. **`src/InvictiScanner/Services/ScanResultWriter.cs`**
   - Added `WriteMetadataOnlyAsync()` method
   - Unchanged: `WriteAsync()` (used by traditional and Option B)

3. **Documentation files added:**
   - `FIRE_AND_FORGET_ARCHITECTURE.md` (comprehensive guide with full diagrams)
   - `CONFIGURATION_GUIDE.md` (environment variables reference)
   - `QUICK_START_FIRE_AND_FORGET.md` (quick reference guide)
   - `CODE_CHANGES_SUMMARY.md` (this file)

---

## 10. No Changes Required

The following files require **NO changes** because they already support the new pattern:

- ✅ `InvictiApiClient.cs` - Already has `LaunchScanAsync()` and `GetIssuesForScanAsync()`
- ✅ `Program.cs` - Already sets up DI correctly
- ✅ `AppSettings.cs` - Configuration loading already works
- ✅ `Models/*.cs` - No schema changes needed
- ✅ `Dockerfile` - Container setup unchanged

---

## Summary

| Aspect | Change | Impact |
|--------|--------|--------|
| **Code added** | 2 methods | Minimal, additive |
| **Breaking changes** | None | Fully backwards compatible |
| **Performance** | Option A now seconds | Major improvement |
| **Resource usage** | Option A: dramatic reduction | Pods exit quickly |
| **Webhook integration** | New feature | Enables async workflow |
| **Default behavior** | Unchanged | No action required for existing users |

