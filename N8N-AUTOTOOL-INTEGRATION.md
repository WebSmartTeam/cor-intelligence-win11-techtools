# COR Cleanup Auto Tool — N8N Integration Contract

**Webhook URL**: `https://n8n.corsolutions.co.uk/webhook/cor-cleanup-autotool`
**Method**: `POST`
**Content-Type**: `application/json`
**Timeout**: 60 seconds (app-side — if no response in 60s, falls back to manual mode)

---

## What the App Sends (POST Body)

The app runs 20 diagnostic checks in parallel, then serialises the entire report as a single JSON object. All property names are **camelCase**.

```json
{
  "systemInfo": {
    "computerName": "DESKTOP-OFFICE01",
    "osEdition": "Windows 11 Pro",
    "osVersion": "10.0.22631",
    "osBuild": "22631.4890",
    "installDate": "2024-03-15T00:00:00",
    "edition": 2,
    "cpuName": "Intel Core i7-13700K",
    "cpuCores": 16,
    "cpuThreads": 24,
    "cpuMaxClockMhz": 5400,
    "gpuName": "NVIDIA GeForce RTX 4070",
    "gpuDriverVersion": "566.36",
    "gpuVramBytes": 12884901888,
    "motherboardManufacturer": "ASUS",
    "motherboardProduct": "ROG STRIX Z790-E",
    "biosVersion": "2803",
    "biosDate": "12/01/2024",
    "totalPhysicalMemoryBytes": 34359738368
  },

  "ramSummary": {
    "dimms": [
      { "manufacturer": "Kingston", "capacityBytes": 17179869184, "speedMhz": 5600, "formFactor": "DIMM", "type": "DDR5" }
    ],
    "totalSlots": 4,
    "usedSlots": 2,
    "maxCapacityBytes": 137438953472,
    "installedCapacityBytes": 34359738368,
    "channelConfig": "Dual Channel"
  },

  "diskHealth": [
    {
      "model": "Samsung SSD 980 PRO 1TB",
      "serialNumber": "S6B...",
      "mediaType": "SSD",
      "sizeBytes": 1000204886016,
      "status": "OK",
      "temperature": 38,
      "healthStatus": "Healthy",
      "smartSupported": true
    }
  ],

  "outdatedDrivers": [
    {
      "deviceName": "Realtek Audio",
      "currentVersion": "6.0.9456.1",
      "latestVersion": "6.0.9539.1",
      "driverDate": "2024-01-15T00:00:00",
      "className": "MEDIA"
    }
  ],

  "battery": null,

  "memoryInfo": {
    "totalPhysicalBytes": 34359738368,
    "availablePhysicalBytes": 8589934592,
    "totalPageFileBytes": 68719476736,
    "availablePageFileBytes": 42949672960,
    "totalVirtualBytes": 140737488355328,
    "availableVirtualBytes": 140737084203008,
    "memoryLoadPercent": 75
  },

  "topMemoryConsumers": [
    { "processName": "chrome", "workingSetBytes": 2147483648, "privateBytes": 1610612736, "instanceCount": 42 },
    { "processName": "Teams", "workingSetBytes": 1073741824, "privateBytes": 805306368, "instanceCount": 6 }
  ],

  "topCpuProcesses": [
    { "processId": 1234, "processName": "chrome", "cpuPercent": 12.5, "workingSetBytes": 2147483648, "isSystem": false },
    { "processId": 5678, "processName": "MsMpEng", "cpuPercent": 8.3, "workingSetBytes": 536870912, "isSystem": true }
  ],

  "networkAdapters": [
    {
      "name": "Ethernet 2",
      "description": "Intel I225-V",
      "type": "Ethernet",
      "status": "Up",
      "speedBps": 1000000000,
      "ipAddress": "192.168.1.50",
      "subnetMask": "255.255.255.0",
      "gateway": "192.168.1.1",
      "dnsServers": ["192.168.1.1", "8.8.8.8"],
      "macAddress": "AA:BB:CC:DD:EE:FF"
    }
  ],

  "publicIp": "203.0.113.42",

  "recentErrors": [
    {
      "source": "Application Error",
      "eventId": 1000,
      "level": "Error",
      "timeGenerated": "2026-02-14T09:15:00Z",
      "message": "Faulting application name: explorer.exe, version 10.0.22631.4890"
    }
  ],

  "recentWarnings": [
    {
      "source": "Disk",
      "eventId": 153,
      "level": "Warning",
      "timeGenerated": "2026-02-13T14:20:00Z",
      "message": "The IO operation at logical block address was retried"
    }
  ],

  "cleanupItems": [
    { "name": "Windows Temporary Files", "category": "WindowsTemp", "estimatedSizeBytes": 1073741824, "path": "C:\\Windows\\Temp" },
    { "name": "Browser Caches", "category": "BrowserCache", "estimatedSizeBytes": 3221225472, "path": null },
    { "name": "Recycle Bin", "category": "RecycleBin", "estimatedSizeBytes": 536870912, "path": null },
    { "name": "Windows Update Cache", "category": "WindowsUpdate", "estimatedSizeBytes": 2147483648, "path": "C:\\Windows\\SoftwareDistribution" }
  ],

  "registryIssues": [
    { "keyPath": "HKLM\\SOFTWARE\\Classes\\CLSID\\{...}", "issue": "Missing DLL reference", "category": "MissingDll", "risk": "Safe" },
    { "keyPath": "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\.xyz", "issue": "Unused file extension", "category": "FileExtension", "risk": "Safe" },
    { "keyPath": "HKLM\\SOFTWARE\\WOW6432Node\\...", "issue": "Orphaned uninstall entry", "category": "UninstallEntry", "risk": "Review" }
  ],

  "installedSoftware": [
    { "name": "Google Chrome", "version": "122.0.6261.112", "publisher": "Google LLC", "installDate": "2024-11-20T00:00:00", "estimatedSizeBytes": 524288000 },
    { "name": "Microsoft Office 365", "version": "16.0.17328.20162", "publisher": "Microsoft Corporation", "installDate": "2024-01-10T00:00:00", "estimatedSizeBytes": 2147483648 }
  ],

  "antivirusProducts": [
    { "displayName": "Windows Defender", "productState": 397568, "isEnabled": true, "isUpToDate": true, "provider": "Microsoft" }
  ],

  "startupItems": [
    { "name": "Microsoft Teams", "command": "\"C:\\Users\\...\\Teams.exe\" --process-start-args", "location": "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", "isEnabled": true, "publisher": "Microsoft Corporation" },
    { "name": "Spotify", "command": "\"C:\\Users\\...\\Spotify.exe\" /minimized", "location": "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", "isEnabled": true, "publisher": "Spotify AB" }
  ],

  "runningServices": [
    { "serviceName": "wuauserv", "displayName": "Windows Update", "status": "Running", "startType": "Manual" },
    { "serviceName": "Spooler", "displayName": "Print Spooler", "status": "Running", "startType": "Automatic" }
  ],

  "recentCrashes": [
    { "timestamp": "2026-02-10T03:45:00Z", "bugCheckCode": "0x0000001E", "bugCheckDescription": "KMODE_EXCEPTION_NOT_HANDLED", "dumpFilePath": "C:\\Windows\\Minidump\\021026-12345-01.dmp" }
  ],

  "bloatwareApps": [
    { "packageName": "Microsoft.XboxApp", "displayName": "Xbox", "category": "Entertainment", "safety": "Safe", "isInstalled": true },
    { "packageName": "Clipchamp.Clipchamp", "displayName": "Clipchamp", "category": "Utility", "safety": "Safe", "isInstalled": true },
    { "packageName": "Microsoft.BingWeather", "displayName": "MSN Weather", "category": "News", "safety": "Safe", "isInstalled": true },
    { "packageName": "Microsoft.WindowsStore", "displayName": "Microsoft Store", "category": "System", "safety": "Dangerous", "isInstalled": true }
  ],

  "generatedAt": "2026-02-14T11:15:30Z",
  "appVersion": "1.0.5"
}
```

### Key Data Points for AI Decision-Making

| Section | What to Look At | Why It Matters |
|---------|----------------|----------------|
| `cleanupItems` | `estimatedSizeBytes` per category | If total > 1 GB, recommend `CLEAN_TEMP` |
| `registryIssues` | Count + risk levels (Safe/Review/Caution) | If Safe count > 5, recommend `FIX_REGISTRY_SAFE`. Only recommend `FIX_REGISTRY_ALL` if many Review items |
| `recentErrors` | Event source, message patterns | System file errors → recommend `RUN_SFC`. Repeated disk errors → don't recommend heavy operations |
| `recentCrashes` | BSOD bug check codes | `KMODE_EXCEPTION` or `IRQL_NOT_LESS_OR_EQUAL` → recommend `RUN_SFC` + `RUN_DISM` |
| `memoryInfo` | `memoryLoadPercent` | > 85% → machine is memory-stressed, note in summary |
| `topCpuProcesses` | High CPU consumers | If telemetry/Copilot processes are hogging CPU → recommend `APPLY_PRIVACY_TWEAKS` or `DISABLE_COPILOT` |
| `bloatwareApps` | Apps where `isInstalled: true` and `safety: "Safe"` | If > 3 safe bloatware installed → recommend `REMOVE_SAFE_BLOATWARE` |
| `antivirusProducts` | `isEnabled`, `isUpToDate` | If AV is out of date or disabled → flag in summary as critical |
| `outdatedDrivers` | Count | Note in summary but no Auto Tool action for this (manual update needed) |
| `networkAdapters` | Status, DNS servers | DNS issues or stale config → recommend `FLUSH_DNS` |
| `startupItems` | Count of enabled items | High startup count → note in summary (no auto action yet, but good context) |

---

## What the App Expects Back (Response Body)

The N8N workflow must return a JSON object with exactly this shape:

```json
{
  "recommendedActionIds": ["CLEAN_TEMP", "FLUSH_DNS", "RUN_SFC", "APPLY_PRIVACY_TWEAKS"],
  "summary": "This machine has 6.9 GB of temporary files, 23 registry issues (18 safe), and 3 recent system file errors in the event log. RAM usage is at 75% with Chrome consuming 2 GB across 42 processes. AV is up to date. Recommend cleaning temp files, flushing DNS, running SFC to address the system file errors, and applying privacy tweaks to reduce background telemetry.",
  "actionReasons": {
    "CLEAN_TEMP": "6.9 GB of temporary files detected across Windows Temp, browser caches, and update cache — safe to remove, will free significant disk space",
    "FLUSH_DNS": "DNS cache may contain stale entries — safe, instant operation with no downside",
    "RUN_SFC": "3 system file integrity errors found in recent event logs including explorer.exe faults — SFC will scan and repair corrupted system files",
    "APPLY_PRIVACY_TWEAKS": "Telemetry and advertising services consume background resources — disabling these reduces unnecessary network and CPU usage"
  }
}
```

### Response Fields Explained

| Field | Type | Required | What It Does in the App |
|-------|------|----------|------------------------|
| `recommendedActionIds` | `string[]` | Yes | Each ID in this array gets **auto-ticked** in the action list. The checkbox is checked and the action gets a cyan "AI Recommended" badge |
| `summary` | `string` | Yes | Displayed in a summary card above the action list. This is the AI's overall assessment shown to the MSP technician. Keep it 2-4 sentences |
| `actionReasons` | `object` | Yes | Key = ActionId, Value = reason string. Shown as grey subtext under each recommended action. Explains **why** this action was recommended for this specific machine |

### What Happens With Each Field

**`recommendedActionIds`** — Actions whose IDs appear here:
- Get their checkbox **ticked** (IsSelected = true)
- Get flagged with `AiRecommended = true` (shows cyan "AI Recommended" badge)
- The action reason from `actionReasons` is shown beneath the action description

**Actions whose IDs are NOT in `recommendedActionIds`**:
- Checkbox is **unticked** (IsSelected = false)
- No badge, no reason shown
- User can still manually tick them

**`summary`**:
- Shown in a card at the top of the action selection screen
- Header says "COR Intelligence AI Assessment"
- This is the first thing the technician reads — make it useful

---

## Available Action IDs

These are the only valid IDs. The AI must pick from this list — any ID not in this list is ignored.

| ActionId | Display Name | Category | Risk | What It Does |
|----------|-------------|----------|------|-------------|
| `CLEAN_TEMP` | Clean Temporary Files | Cleanup | **Safe** | Removes Windows temp, browser caches, recycle bin, prefetch, thumbnails, update logs |
| `FIX_REGISTRY_SAFE` | Fix Safe Registry Issues | Cleanup | Low | Fixes only Safe-risk registry entries (missing DLLs, unused extensions, dead shortcuts) |
| `FIX_REGISTRY_ALL` | Fix All Registry Issues | Cleanup | **Medium** | Fixes ALL detected registry issues including Review and Caution (backup created first) |
| `RUN_SFC` | System File Checker (SFC) | Repair | Low | Runs `SFC /scannow` to repair corrupted Windows system files |
| `RUN_DISM` | DISM Health Restore | Repair | Low | Runs `DISM /Online /RestoreHealth` to repair the Windows component store |
| `FLUSH_DNS` | Flush DNS Cache | Network | **Safe** | Clears DNS resolver cache — fixes stale/incorrect DNS entries |
| `RESET_NETWORK` | Reset Network Stack | Network | **Medium** | Resets TCP/IP and Winsock — **requires reboot** |
| `RESET_WINDOWS_UPDATE` | Reset Windows Update | Repair | **Medium** | Stops update services, clears cache, re-registers DLLs |
| `CLEAR_PRINT_SPOOLER` | Clear Print Spooler | Repair | **Safe** | Stops spooler, clears stuck jobs, restarts spooler |
| `DISABLE_COPILOT` | Disable Windows Copilot | Privacy | Low | Registry policy + AppX package removal |
| `DISABLE_RECALL` | Disable Windows Recall | Privacy | Low | Registry policy (24H2 Copilot+ PCs only) |
| `APPLY_PRIVACY_TWEAKS` | Apply Privacy Settings | Privacy | Low | Disables telemetry, advertising ID, Start menu suggestions, Bing search, lock screen ads |
| `REMOVE_SAFE_BLOATWARE` | Remove Safe Bloatware | Cleanup | **Medium** | Removes AppX packages marked Safe: Xbox, Clipchamp, News, Weather, Solitaire, etc. |

### Risk Level Guidelines for AI

- **Safe** (green) — Zero risk, always fine to recommend: `CLEAN_TEMP`, `FLUSH_DNS`, `CLEAR_PRINT_SPOOLER`
- **Low** (blue) — Very low risk, recommend when evidence supports it: `FIX_REGISTRY_SAFE`, `RUN_SFC`, `RUN_DISM`, `DISABLE_COPILOT`, `DISABLE_RECALL`, `APPLY_PRIVACY_TWEAKS`
- **Medium** (orange) — Some risk, only recommend when clearly needed: `FIX_REGISTRY_ALL`, `RESET_NETWORK`, `RESET_WINDOWS_UPDATE`, `REMOVE_SAFE_BLOATWARE`
- **High** (red) — Currently no High-risk actions in the catalogue

**General rule**: Default to recommending Safe + Low actions. Only recommend Medium if the diagnostic data clearly justifies it (e.g. don't recommend `RESET_NETWORK` unless there are actual network problems).

---

## Error Handling / Graceful Degradation

The app handles all failure cases — the N8N workflow just needs to return valid JSON or let the request fail:

| Scenario | What Happens in the App |
|----------|------------------------|
| N8N returns valid JSON | AI recommendations loaded, actions auto-ticked, summary shown |
| N8N returns invalid JSON | Falls back to manual mode — "AI consultation failed — select actions manually" |
| N8N returns HTTP error (4xx/5xx) | Falls back to manual mode |
| N8N doesn't respond within 60 seconds | Falls back to manual mode — "AI consultation timed out — select actions manually" |
| N8N is unreachable (DNS/network) | Falls back to manual mode |
| `recommendedActionIds` contains unknown IDs | Unknown IDs silently ignored, valid IDs still ticked |
| `recommendedActionIds` is empty | No actions ticked, summary still shown |
| `actionReasons` missing a key | Action is ticked but no reason text shown (just blank) |

**The tool works 100% without AI** — users can always manually tick actions and press GO.

---

## N8N Workflow Design Notes

### Suggested N8N Flow

```
Webhook (POST) → Code Node (format prompt) → Claude API → Code Node (parse response) → Respond to Webhook
```

### Claude System Prompt (for the N8N Code Node)

Give Claude the diagnostic JSON and a system prompt like:

> You are a Windows system health analyst for COR Intelligence. You receive a diagnostic report from COR Cleanup (a Windows repair tool used by MSP technicians). Analyse the report and recommend which remediation actions to run.
>
> Return ONLY a JSON object with three fields: recommendedActionIds (array of action ID strings), summary (2-4 sentence assessment), and actionReasons (object mapping action IDs to reason strings).
>
> Available action IDs: CLEAN_TEMP, FIX_REGISTRY_SAFE, FIX_REGISTRY_ALL, RUN_SFC, RUN_DISM, FLUSH_DNS, RESET_NETWORK, RESET_WINDOWS_UPDATE, CLEAR_PRINT_SPOOLER, DISABLE_COPILOT, DISABLE_RECALL, APPLY_PRIVACY_TWEAKS, REMOVE_SAFE_BLOATWARE.
>
> Be conservative — prefer Safe and Low risk actions. Only recommend Medium risk actions when the data clearly justifies it. Always explain your reasoning in actionReasons.

### Response Format Enforcement

Make sure the N8N response node returns **only** the JSON object — no markdown code fences, no extra text. The app deserialises with `System.Text.Json` which expects clean JSON.

If Claude wraps the response in ```json ... ```, use a Code Node to strip it:

```javascript
// Strip markdown fences if present
let text = $input.first().json.text;
text = text.replace(/^```json\s*/i, '').replace(/\s*```$/i, '').trim();
return [{ json: JSON.parse(text) }];
```
