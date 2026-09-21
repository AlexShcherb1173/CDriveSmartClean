# CDriveSmartClean — Business Logic Specification v1.1

**Status:** Baseline  
**Version:** 1.1  
**Date:** 2026-09-22  
**Scope:** Product behavior and business rules for system-drive analysis, cleanup planning, user approval, verification, and AI-assisted explanation.

## 1. Product definition

CDriveSmartClean is a local-first Windows application that explains why the system drive is full, estimates what space can actually be reclaimed, and gives the user a controlled set of cleanup options.

The product is a **universal system-drive intelligence tool**, not a cleaner centered on any specific IDE, browser, developer stack, game, or vendor.

The core process is:

```text
Discover
  -> Measure
  -> Classify
  -> Explain
  -> Estimate reclaimable space
  -> Assess risk and confidence
  -> AI analysis (optional, advisory only)
  -> User decision
  -> Revalidate
  -> Execute approved action
  -> Verify actual reclaimed space
```

A `Finding` is a fact about disk usage. A finding is **not** automatically junk and is **not** automatically removable.

## 2. Product invariants

The following rules are mandatory:

1. The first analysis pass is read-only.
2. The user always makes the final decision to remove, uninstall, dehydrate, disable, prune, or otherwise change data.
3. AI is analysis-only. It cannot execute cleanup actions.
4. AI cannot create arbitrary executable actions.
5. AI cannot lower `Risk`, remove `ProtectionState`, or bypass policy.
6. Unknown data must remain visible.
7. Large size alone is never a deletion reason.
8. Old age alone is never a deletion reason.
9. Logical size is not treated as reclaimable size.
10. System data and user data are protected by default.
11. Every executable cleanup action must explain expected impact before execution.
12. Every selected action is revalidated immediately before execution.
13. Every executed action is verified afterward.
14. Direct deletion of critical Windows components is not allowed.
15. Specialized application knowledge may enrich a finding, but it must never determine scan completeness.
16. CDriveSmartClean must work meaningfully even when it recognizes none of the installed applications.

## 3. System-drive scope

The engine discovers the actual Windows system volume. It must not hard-code `C:\`.

The product UI may use the marketing hook **"C: U OK?"**, but the engine operates on the detected system volume.

The scan scope includes all physically allocated storage attributable to the system volume, including:

- Windows and system-managed data;
- installed applications;
- application data;
- user data;
- temporary data and caches;
- logs and diagnostics;
- backups and snapshots;
- virtual disks and virtualized storage;
- cloud-backed files and placeholders;
- recycle bin;
- hidden/system folders;
- unknown directories and files;
- filesystem-reserved or otherwise unattributed usage.

The scanner must not silently traverse onto another mounted volume through a junction, mount point, or reparse point.

## 4. Primary user questions

A successful scan must answer four questions:

1. **What is using my system drive?**
2. **How much physical space is actually being used?**
3. **How much can realistically be reclaimed, and with what risk?**
4. **What exactly will happen if I approve a cleanup action?**

## 5. Two separate storage views

### 5.1 Where is my space?

This view explains physical usage of the system volume.

Example categories:

- Windows/System
- Applications
- Application Data
- User Data
- Temporary Data
- Caches
- Logs & Diagnostics
- Installers & Archives
- Backups & Snapshots
- Virtual Storage
- Cloud-backed Data
- Recycle Bin
- Filesystem/Reserved
- Unknown

The category totals should reconcile as closely as possible to the volume's actual used space.

### 5.2 What can I reclaim?

This is a separate view. It groups potential actions by reclaimability and risk, for example:

- low-risk/reproducible;
- requires review;
- user decision;
- conditional;
- unknown;
- protected/non-actionable.

Physical usage and reclaimable usage must never be presented as the same number.

## 6. Storage accounting

### 6.1 VolumeAccounting

```text
VolumeAccounting
{
    CapacityBytes
    UsedBytes
    FreeBytes

    AccountedAllocatedBytes
    UnknownAllocatedBytes
    FilesystemReservedBytes
    UnattributedBytes

    CoveragePercent
}
```

### 6.2 Coverage

The product reports how much used space it can explain.

Example:

```text
Storage explained: 96.8%
Unattributed: 3.2%
```

The engine must never invent a classification merely to reach 100%.

### 6.3 Unknown vs Unattributed

- **Unknown:** the filesystem object was found and measured, but its purpose/owner could not be classified.
- **Unattributed:** physical disk usage could not be reliably associated with a discovered object, for example filesystem metadata, inaccessible areas, reserved structures, or measurement gaps.

Both must be visible.

## 7. Size model

Every significant finding uses distinct size concepts.

```text
SizeMetrics
{
    LogicalBytes
    AllocatedBytes
    ExclusiveAllocatedBytes
}
```

### LogicalBytes

The logical file/content size reported by the filesystem.

### AllocatedBytes

The physical allocation consumed on the volume.

### ExclusiveAllocatedBytes

The allocation attributable exclusively to this object after accounting for shared/hard-linked data where possible.

The product must handle:

- NTFS compression;
- sparse files;
- hard links;
- reparse points;
- cloud placeholders;
- virtual disk containers;
- deduplicated/shared physical allocations where detectable.

## 8. Finding model

`Finding` is the central domain object.

```text
Finding
{
    Id
    ScanSessionId

    Scope
    PrimaryCategory
    Facets[]

    PathIdentity
    DisplayName

    SizeMetrics

    FileCount
    DirectoryCount

    LargestChildren[]
    DominantFileTypes[]

    CreatedAt
    LastModifiedAt

    Ownership
    ApplicationAttribution
    ActivityState

    ReclaimEstimate
    RiskAssessment
    Confidence
    ProtectionState

    Evidence[]
    Dependencies[]

    AvailableActions[]

    AttentionPriority
    CleanupValue
}
```

A finding may have zero cleanup actions.

## 9. Primary categories

A finding has one primary category:

```text
SYSTEM
APPLICATION
APPLICATION_DATA
USER_DATA
TEMPORARY
CACHE
LOG_DIAGNOSTIC
INSTALLER_ARCHIVE
BACKUP_SNAPSHOT
VIRTUAL_STORAGE
CLOUD_BACKED
RECYCLE_BIN
FILESYSTEM_OVERHEAD
UNKNOWN
```

Primary category describes what the data fundamentally is. Additional characteristics are represented as facets.

## 10. Facets

A finding may have multiple facets:

```text
LARGE
OLD
RECENT
GROWING
DUPLICATE_CANDIDATE
ORPHAN_CANDIDATE
CACHE_LIKE
TEMP_LIKE
USER_CONTENT
COMPRESSED
SPARSE
HARD_LINKED
CLOUD_PLACEHOLDER
VIRTUALIZED
ACTIVE
SYSTEM_PROTECTED
UNKNOWN_OWNER
```

Example:

```text
PrimaryCategory = USER_DATA
Facets = [LARGE, OLD, DUPLICATE_CANDIDATE]
```

Facets are signals, not deletion permissions.

## 11. Evidence model

Every non-trivial classification or recommendation should expose evidence.

Example:

```text
Evidence:
- 41.7 GB physically allocated
- Located under a per-user application-data root
- 97% of content matches temporary/cache-like patterns
- Files are actively changing
- Related application could not be verified
```

Evidence supports deterministic logic, user explanation, auditing, and AI analysis.

## 12. Application attribution and knowledge enrichment

The universal scanner works independently of application recognition.

After scanning, an attribution/enrichment layer may identify an owning application, publisher, purpose, reproducibility, or supported cleanup mechanism using sources such as:

- installed-application metadata;
- MSIX/AppX metadata;
- registry uninstall entries;
- executable metadata;
- running processes and services;
- declarative known-path rules;
- filesystem signatures.

If attribution fails, the finding remains valid as `UNKNOWN` or as a generic category.

Specialized knowledge about browsers, games, IDEs, Docker, Python, media software, office software, cloud clients, and other products is strictly an enrichment layer.

## 13. Universal analyses

### 13.1 Largest objects

The engine produces:

- largest directories;
- largest files;
- largest file groups;
- largest application footprints;
- largest unknown areas.

Thresholds should scale with volume size rather than rely on a single hard-coded file size.

### 13.2 Duplicate analysis

Duplicate detection must not use filename equality as proof.

Preferred pipeline:

```text
same logical size
  -> fast fingerprint
  -> strong hash for candidates
  -> duplicate group
```

Rules:

- hard links are not counted as ordinary duplicates;
- cloud placeholders must not be hydrated solely to calculate a hash;
- shared allocation must not be counted twice as reclaimable;
- duplicate deletion remains a user decision.

### 13.3 Dormant analysis

The engine may identify data that has not changed for long periods.

Age is informational only. Old personal data is not considered junk because it is old.

### 13.4 Growth analysis

When historical scans exist, the engine compares snapshots.

Example:

```text
Application Data
Previous: 42 GB
Current:  91 GB
Growth:  +49 GB / 17 days
```

Rapid growth may produce a `GROWING` facet and a higher attention priority.

### 13.5 Orphan analysis

Data may be marked `ORPHAN_CANDIDATE` when it appears related to software no longer installed.

This never implies safe deletion by itself.

### 13.6 Virtual storage

VHD, VHDX, VMDK, VM/container stores, database-like stores, and other large virtualized containers are recognized as virtual storage where possible.

The outer file size alone is not assumed to be reclaimable.

### 13.7 Cloud-backed storage

The engine should distinguish states such as:

- online-only;
- placeholder;
- partially hydrated;
- locally available;
- always available locally.

A 100 GB logical cloud tree that occupies 300 MB locally must never be presented as 100 GB reclaimable.

## 14. Reclaimability

```text
ReclaimEstimate
{
    Kind

    MinimumBytes
    ExpectedBytes
    MaximumBytes

    Basis
    Preconditions[]

    Confidence

    RequiresRestart
    RequiresCompaction

    OverlapGroupIds[]
}
```

Kinds:

```text
EXACT
ESTIMATED
CONDITIONAL
USER_DECISION
UNKNOWN
NONE
```

Reclaimability is not equivalent to allocated size.

## 15. Overlap accounting

Findings may overlap hierarchically or logically.

Example:

```text
Downloads             40 GB
Downloads\ISO         15 GB
Downloads\ISO\old.iso 9 GB
```

The cleanup planner must calculate **net reclaimable space** from unique physical allocations and must not sum overlapping findings naively.

## 16. Risk model

```text
RiskAssessment
{
    Level

    DataCriticality
    SystemImpact
    Reproducibility
    ActiveUse
    Reversibility
    Confidence

    Reasons[]
}
```

Risk levels:

```text
SAFE
LOW
MEDIUM
HIGH
CRITICAL
```

Risk answers: **What could go wrong if this action is performed?**

## 17. Protection state

Protection is independent of risk.

```text
NORMAL
REVIEW_REQUIRED
PROTECTED
BLOCKED
```

Examples:

- a normal temporary cache may be `NORMAL`;
- user files are typically `REVIEW_REQUIRED`;
- sensitive system-managed data may be `PROTECTED`;
- direct deletion of critical Windows locations may be `BLOCKED`.

A blocked finding can still be measured and explained.

## 18. Confidence

```text
VERIFIED
HIGH
MEDIUM
LOW
UNKNOWN
```

Confidence answers: **How certain are we that we understand this object and the proposed interpretation?**

Risk and confidence must not be conflated.

## 19. CleanupAction model

Only deterministic product logic may create executable cleanup actions.

```text
CleanupAction
{
    Id
    FindingId

    ActionKind

    Description
    ImpactDescription

    EstimatedReclaim

    Risk
    ProtectionState

    Preconditions[]
    SafetyChecks[]

    RequiresAdmin
    RequiresApplicationShutdown
    RequiresRestart

    Reversibility

    ExecutorKind
    VerificationStrategy
}
```

There is no generic user-facing "delete anything" action.

## 20. Supported action model

Examples of predefined action kinds:

```text
MOVE_TO_RECYCLE_BIN
DELETE_APPROVED_PATH
CLEAR_APPROVED_CACHE
WINDOWS_NATIVE_CLEANUP
UNINSTALL_APPLICATION
CLOUD_DEHYDRATE
DISABLE_HIBERNATION
PURGE_TOOL_CACHE
REMOVE_APPROVED_SNAPSHOT
```

The business layer must not expose an arbitrary command or arbitrary PowerShell execution action.

For ordinary user files, the default destructive operation should prefer Recycle Bin where technically appropriate. Permanent deletion requires an explicit, distinct user choice.

## 21. User-decision contract

All cleanup actions require a user decision.

Recommended actions may be prioritized or grouped, but the product must not silently decide that data is unnecessary.

A valid user decision records:

- selected `ActionId`;
- scan/session identity;
- visible impact summary;
- risk presented to the user;
- timestamp;
- approval state.

AI recommendations do not count as user approval.

## 22. Cleanup plan

```text
CleanupPlan
{
    ScanSessionId

    SelectedActionIds[]

    GrossEstimatedReclaim
    NetEstimatedReclaim

    RiskSummary

    RequiresAdmin
    RequiresRestart

    CreatedAt
}
```

The planner resolves overlap before showing final expected reclaim.

## 23. Pre-execution revalidation

Immediately before execution the system checks that the target still matches the scan result.

Checks may include:

- target still exists;
- same volume/object identity;
- expected file identity where supported;
- path has not become a reparse/junction redirect;
- size/state changes;
- application/process activity;
- dependencies;
- risk and protection state;
- action is still allowed by policy.

If assumptions no longer hold, execution is blocked and re-analysis is required.

## 24. Verification

Every executed action produces a verification result.

```text
VerificationResult
{
    FreeSpaceBefore
    FreeSpaceAfter

    ActualDriveGain

    ItemAllocatedBefore
    ItemAllocatedAfter

    ExpectedReclaim
    Difference

    Status
}
```

The product reports actual reclaimed space rather than assuming the estimate was correct.

## 25. Priority model

The UI uses two different rankings.

### AttentionPriority

Ranks what is most important for understanding why the drive is full.

### CleanupValue

Ranks actions by a combination of:

- expected net reclaim;
- confidence;
- safety;
- reversibility;
- disruption/cost.

A large personal-data directory may have high attention priority but low cleanup value.

## 26. Scan modes

### Quick Scan

Fast overview of volume health and major usage areas.

### Smart Scan

Default mode. Includes:

- system-volume inventory;
- physical allocation accounting;
- classification;
- reclaim estimation;
- risk/confidence;
- knowledge enrichment;
- history comparison when available.

### Deep Scan

Adds expensive analyses such as:

- strong duplicate hashing;
- deeper orphan analysis;
- deeper file-group analysis;
- extended historical analysis.

## 27. Historical analysis

CDriveSmartClean stores local scan metadata/snapshots so it can answer questions such as:

- What grew since the last scan?
- Which category added 30 GB this week?
- Is an unknown folder expanding rapidly?

The application does not need to retain file contents to provide history.

## 28. AI analyst contract

AI is optional and advisory.

AI receives a sanitized, structured storage report, not unrestricted filesystem access.

AI may:

- explain disk usage;
- correlate findings;
- identify unusual patterns;
- explain risk and uncertainty;
- suggest an order for user review;
- compare historical scans;
- explain existing deterministic cleanup actions;
- form hypotheses about unknown data.

AI must not:

- delete or modify data;
- launch processes;
- invoke PowerShell, CMD, registry tools, or privileged APIs;
- create arbitrary cleanup actions;
- downgrade risk;
- remove protection;
- mark user data as unnecessary;
- select actions for the user;
- convert an unknown finding into a safe action.

An AI insight may reference only existing deterministic `FindingId` and `ActionId` values.

## 29. AI privacy contract

By default AI does not receive:

- document contents;
- source code contents;
- image/media contents;
- passwords or tokens;
- browser history;
- cookies;
- database contents.

Sensitive paths/names should be sanitized or replaced with stable aliases before cloud AI processing.

CDriveSmartClean must remain fully functional with AI disabled.

## 30. Reference regression scenario

The historical NOTEWS cleanup is retained as one regression scenario, not as the product definition.

It validates that the engine can:

- detect severe system-drive pressure;
- distinguish true physical/reclaimable data from misleading logical size;
- rank high-value reclaim opportunities above large but low-impact placeholders;
- avoid recommending direct deletion of protected or valuable data;
- verify actual free-space change after cleanup.

Developer-heavy software is only one scenario among home, office, gaming, cloud-heavy, media, aging-Windows, and unknown-growth systems.

## 31. Acceptance criteria for v1.1

The v1.1 business logic is considered satisfied when the product can:

1. explain the majority of physical usage on an arbitrary Windows system volume;
2. explicitly report remaining unattributed usage;
3. avoid crossing onto unrelated volumes through reparse structures;
4. distinguish logical, allocated, and reclaimable size;
5. avoid double-counting hard-linked/shared/overlapping data;
6. keep cloud-placeholder logical size separate from local physical usage;
7. show unknown data rather than hiding it;
8. show large user files without labeling them junk;
9. detect duplicate candidates using content-based verification;
10. treat age as a signal, not deletion permission;
11. recognize protected Windows/system-managed areas;
12. calculate net reclaim without overlap inflation;
13. require user approval for every cleanup action;
14. prevent AI from executing or authorizing cleanup;
15. revalidate targets before execution;
16. verify actual reclaimed space after execution;
17. provide useful analysis even without any specialized application recognizers.

## 32. Non-goals for v1

The following are intentionally out of scope for the first product version:

- autonomous cleanup;
- always-on background deletion;
- kernel filesystem filter drivers;
- arbitrary scripting plugins;
- AI agents with operating-system tools;
- remote/cloud scanning of the user's filesystem;
- hidden deletion based solely on heuristics.

---

This document is the business-logic baseline for Architecture v1 and Security Model v1.
