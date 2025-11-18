# Invicti Scanner for SecureCodeBox - Fire-and-Forget Pattern

## 📚 Documentation

All documentation has been organized in the `docs/` folder. Start here:

### Quick Navigation

- **🚀 [Quick Start](./docs/QUICK_START_FIRE_AND_FORGET.md)** - Get up and running in 5 minutes
- **📖 [Complete Index](./docs/INDEX.md)** - Full documentation navigation guide
- **🏗️ [Architecture](./docs/FIRE_AND_FORGET_ARCHITECTURE.md)** - Complete technical architecture with diagrams
- **⚙️ [Configuration Guide](./docs/CONFIGURATION_GUIDE.md)** - Environment variables reference
- **💻 [Code Changes](./docs/CODE_CHANGES_SUMMARY.md)** - Implementation details for developers
- **🎨 [Visual Reference](./docs/VISUAL_REFERENCE.md)** - Execution flow diagrams and timelines
- **✅ [Implementation Status](./docs/IMPLEMENTATION_COMPLETE.md)** - What was completed

---

## What Is This?

This project implements a **fire-and-forget pattern** for the Invicti Scanner integration with SecureCodeBox. Instead of blocking CI/CD pipelines while scans complete (15-120+ minutes), the scanner launches scans asynchronously and returns immediately.

### The Problem

**Traditional Approach:**
```
Launch Scan → Poll for 15-120+ minutes → Block CI/CD → Get Results
```

### The Solution

**Fire-and-Forget with Webhooks:**
```
Launch Scan → Return immediately (5s) → Invicti runs scan async → 
Webhook triggers results collection → Findings uploaded
```

---

## Key Features

✅ **Fast** - CI/CD unblocked in ~5 seconds (vs 15-120+ minutes)  
✅ **Asynchronous** - Invicti runs independently  
✅ **Scalable** - Unlimited concurrent scans  
✅ **On-Prem Compatible** - Uses Invicti Enterprise webhooks  
✅ **Backwards Compatible** - Default behavior unchanged  
✅ **Fully Documented** - 15,000+ words across 8 guides  

---

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│ CI/CD Pipeline                                              │
│  ├─ Trigger scan with SCB_SCANNER__FIREANDFORGET=true       │
│  └─ Returns in ~5 seconds ✓ (FAST!)                         │
└─────────────────────────────────────────────────────────────┘
                           │
          ┌────────────────┴────────────────┐
          │                                 │
    [Immediately returns]          [Invicti continues scanning]
    Pod exits in seconds          (15 minutes - 8 hours)
                                  
                                  When complete...
                                  └─ Webhook fires
                                     └─ GitHub Actions triggered
                                        └─ Fetch results
                                           └─ Upload to SecureCodeBox
```

---

## Quick Start

### 1. Enable Fire-and-Forget
```yaml
env:
  - name: SCB_SCANNER__FIREANDFORGET
    value: "true"
```

### 2. Configure Invicti Webhook
Navigate to Invicti Admin Console → Settings → Webhooks:
- **URL:** `https://securecodeboxapi.your-domain/api/v1/webhooks/invicti-scan-complete`
- **Event:** `Scan.Completed`
- **Auth:** Bearer Token

### 3. Create GitHub Actions Workflow
See [QUICK_START_FIRE_AND_FORGET.md](./docs/QUICK_START_FIRE_AND_FORGET.md) for complete workflow template.

---

## File Structure

```
SecureCodeBox-invicti-enterprise/
├── docs/                              ← ALL DOCUMENTATION
│   ├── INDEX.md                       (Navigation guide)
│   ├── README.md                      (Implementation summary)
│   ├── QUICK_START_FIRE_AND_FORGET.md (3-step setup)
│   ├── FIRE_AND_FORGET_ARCHITECTURE.md (Technical architecture)
│   ├── CONFIGURATION_GUIDE.md         (Environment variables)
│   ├── CODE_CHANGES_SUMMARY.md        (Code modifications)
│   ├── VISUAL_REFERENCE.md            (Flow diagrams)
│   └── IMPLEMENTATION_COMPLETE.md     (Status & checklist)
│
├── src/InvictiScanner/
│   ├── Services/
│   │   ├── ScanOrchestrator.cs        (MODIFIED - fire-and-forget logic)
│   │   ├── ScanResultWriter.cs        (MODIFIED - metadata-only output)
│   │   └── ...
│   └── ...
│
├── README.md                          (This file)
├── Dockerfile
├── invicti.json
└── ...
```

---

## Documentation Overview

| Document | Purpose | Time |
|----------|---------|------|
| **QUICK_START_FIRE_AND_FORGET.md** | Get started fast | 5 min |
| **CONFIGURATION_GUIDE.md** | Environment variable reference | 10 min |
| **FIRE_AND_FORGET_ARCHITECTURE.md** | Complete technical design | 20 min |
| **VISUAL_REFERENCE.md** | Flow diagrams & timelines | 15 min |
| **CODE_CHANGES_SUMMARY.md** | Code implementation details | 10 min |
| **IMPLEMENTATION_COMPLETE.md** | Status & testing checklist | 5 min |
| **INDEX.md** | Navigation & learning paths | 5 min |

**Total:** ~70 minutes for complete understanding (or ~10 minutes for quick setup)

---

## Code Changes Summary

### Modified Files
1. **`src/InvictiScanner/Services/ScanOrchestrator.cs`**
   - Added `ResolveFireAndForgetMode()` method
   - Updated `RunAsync()` for fire-and-forget execution path
   - ~50 lines added

2. **`src/InvictiScanner/Services/ScanResultWriter.cs`**
   - Added `WriteMetadataOnlyAsync()` method
   - Writes empty findings.json + metadata with scan ID
   - ~30 lines added

### Key Changes
- ✅ No breaking changes
- ✅ Fully backwards compatible
- ✅ Default behavior unchanged
- ✅ Fire-and-forget is opt-in

---

## Configuration

### Enable Fire-and-Forget (Option A)
```bash
SCB_ACTION=Scan
SCB_SCANNER__FIREANDFORGET=true
SCB_INVICTI__URL=https://invicti-onprem.your-domain.com
SCB_INVICTI__APIKEY=your-api-token
SCB_SCANNER__OUTPUTPATH=/home/scanner/results
```

### Results Collection (Option B - via GitHub Actions)
```bash
SCB_ACTION=Issues
SCB_SCAN__ID=550e8400-e29b-41d4-a716-446655440000
SCB_INVICTI__URL=https://invicti-onprem.your-domain.com
SCB_INVICTI__APIKEY=your-api-token
SCB_SCANNER__OUTPUTPATH=/tmp/results
```

---

## Performance Comparison

| Metric | Traditional | Fire-and-Forget |
|--------|-------------|-----------------|
| **CI/CD Duration** | 15-120 minutes | ~5 seconds |
| **Pod Lifetime** | 15-120 minutes | ~5 seconds |
| **Pipeline Blocking** | Yes | No |
| **Resource Usage** | High | Minimal |
| **Result Delivery** | Synchronous | Asynchronous |

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success (scan launched or issues fetched) |
| `2` | Failed to launch scan or invalid config |
| `3` | Scan failed (traditional blocking mode only) |
| `130` | Operation cancelled |

---

## Next Steps

1. **Read** → Start with [QUICK_START_FIRE_AND_FORGET.md](./docs/QUICK_START_FIRE_AND_FORGET.md)
2. **Configure** → Set up environment variables and webhooks
3. **Test** → Verify fast launch and webhook delivery
4. **Deploy** → Update production configuration
5. **Monitor** → Check logs during first scans

---

## Documentation

All comprehensive documentation is in the [`docs/`](./docs/) folder:

- 📖 [Read the Complete Documentation](./docs/INDEX.md)
- 🚀 [Get Started in 5 Minutes](./docs/QUICK_START_FIRE_AND_FORGET.md)
- 🏗️ [Understand the Architecture](./docs/FIRE_AND_FORGET_ARCHITECTURE.md)

---

## Technical Details

### Fire-and-Forget Decision Point
When `SCB_SCANNER__FIREANDFORGET=true`:
1. Launch scan in Invicti
2. Skip polling
3. Write metadata-only JSON
4. Exit immediately (code 0)

### Traditional Blocking Mode (default)
When `SCB_SCANNER__FIREANDFORGET=false` or not set:
1. Launch scan in Invicti
2. Poll every 15 seconds
3. Wait for completion
4. Fetch all issues
5. Write complete findings.json
6. Exit (code 0 or 3)

---

## Support

### Troubleshooting
- **Configuration issues** → See [CONFIGURATION_GUIDE.md](./docs/CONFIGURATION_GUIDE.md)
- **Architecture questions** → See [FIRE_AND_FORGET_ARCHITECTURE.md](./docs/FIRE_AND_FORGET_ARCHITECTURE.md)
- **Code questions** → See [CODE_CHANGES_SUMMARY.md](./docs/CODE_CHANGES_SUMMARY.md)
- **General help** → See [INDEX.md](./docs/INDEX.md)

---

## Version

- **Version:** 1.0.0
- **Release Date:** November 18, 2025
- **.NET Target:** 8.0
- **Backwards Compatible:** Yes

---

## Implementation Status

✅ Code implementation complete  
✅ Documentation complete  
✅ Backwards compatibility verified  
✅ Ready for production  

---

**👉 Start here:** [Read the Quick Start Guide](./docs/QUICK_START_FIRE_AND_FORGET.md)
