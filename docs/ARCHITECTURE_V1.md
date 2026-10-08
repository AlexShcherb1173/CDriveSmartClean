# CDriveSmartClean — Architecture v1

**Status:** Baseline  
**Version:** 1.0  
**Date:** 2026-09-22  
**Depends on:** `docs/BUSINESS_LOGIC_V1_1.md`

## 1. Architecture goals

CDriveSmartClean is designed as a **local-first modular monolith** with strict privilege separation.

The architecture must support:

- universal system-volume scanning;
- accurate physical-storage accounting;
- deterministic classification and reclaim estimation;
- safe cleanup planning;
- explicit user approval;
- privileged execution only when required;
- post-action verification;
- optional AI analysis without execution privileges;
- future growth without coupling the core to specific applications.

The architecture prioritizes correctness, safety, explainability, testability, and Windows-native behavior over premature distribution.

## 2. Technology baseline

Baseline technology:

- **Language/runtime:** C# / modern .NET
- **Desktop:** WPF + MVVM for v1
- **Persistence:** SQLite
- **Windows integration:** Win32/NTFS/Windows APIs through isolated platform adapters
- **IPC:** authenticated local named pipes or an equivalently restricted local IPC mechanism
- **Testing:** unit + integration + synthetic filesystem fixtures + temporary virtual disks
- **Packaging:** Windows desktop packaging to be finalized later; Microsoft Store compatibility is a future delivery concern

The business/domain layers must not depend on WPF, SQLite, AI SDKs, or Windows UI frameworks.

### Current implementation support (F1-11)

F1-11 Windows native traversal requires an **x64 .NET process**. Platform.Windows and its native integration tests are built with `PlatformTarget=x64`. The platform adapter is currently a class library; future executable/installer publication requires a compatible x64 process.

Native Windows ARM64 and x86 support are not implemented or verified. Running an x64 process under emulation on ARM64 is not native ARM64 support and is not tested here. Real cross-volume mount-point redirection and ReFS integration remain untested. Runtime fail-closed architecture and ABI checks remain defense in depth, not proof of non-x64 compatibility.

## 3. High-level system

```text
+---------------------------------------------------+
|                   Desktop UI                      |
|             non-elevated by default               |
+--------------------------+------------------------+
                           |
                           v
+---------------------------------------------------+
|             Application Orchestrator              |
| scan / analysis / plan / history / user decisions |
+----------+----------------------+-----------------+
           |                      |
           v                      v
+---------------------+   +--------------------------+
|   Scan / Analysis   |   |      AI Analyst         |
| deterministic       |   | optional, advisory only |
+----------+----------+   +--------------------------+
           |
           v
+---------------------------------------------------+
|           Storage Intelligence Pipeline           |
| inventory -> accounting -> classification         |
| attribution -> enrichment -> reclaim -> policy    |
+--------------------------+------------------------+
                           |
                           v
                       Findings
                           |
                           v
                    Cleanup Planner
                           |
                           v
                         USER
                    selects actions
                           |
                           v
                    Policy Validator
                           |
                           v
+---------------------------------------------------+
|            Privileged Executor Host               |
| elevated only when needed                         |
| predefined actions only                           |
+--------------------------+------------------------+
                           |
                           v
                        Verifier
                           |
                           v
                    Local History DB
```

## 4. Process model

### 4.1 Main desktop process

Runs non-elevated by default.

Responsibilities:

- desktop UI;
- scan orchestration;
- read-only analysis;
- finding presentation;
- cleanup-plan construction;
- user approval capture;
- local history;
- AI analysis;
- communication with the privileged executor.

The main process must not remain permanently elevated.

### 4.2 Privileged executor process

A separate executable, conceptually:

```text
CDriveSmartClean.Executor.exe
```

It is started through UAC only for actions that require elevation.

Responsibilities:

- accept a typed, validated action manifest;
- independently revalidate the action;
- execute a known action type;
- return a structured result;
- never execute arbitrary shell commands received from the UI or AI.

This process is a security trust boundary.

## 5. Repository/project layout

Proposed solution layout:

```text
CDriveSmartClean/
|
|-- src/
|   |-- CDriveSmartClean.Domain/
|   |-- CDriveSmartClean.Application/
|   |-- CDriveSmartClean.Scan/
|   |-- CDriveSmartClean.Analysis/
|   |-- CDriveSmartClean.Knowledge/
|   |-- CDriveSmartClean.Cleanup/
|   |-- CDriveSmartClean.Platform.Windows/
|   |-- CDriveSmartClean.Executor/
|   |-- CDriveSmartClean.AI/
|   |-- CDriveSmartClean.Persistence/
|   `-- CDriveSmartClean.Desktop/
|
|-- tests/
|   |-- CDriveSmartClean.Domain.Tests/
|   |-- CDriveSmartClean.Scan.Tests/
|   |-- CDriveSmartClean.Analysis.Tests/
|   |-- CDriveSmartClean.Cleanup.Tests/
|   |-- CDriveSmartClean.Windows.IntegrationTests/
|   `-- CDriveSmartClean.Security.Tests/
|
|-- rules/
|   `-- builtin/
|
|-- docs/
|   |-- BUSINESS_LOGIC_V1_1.md
|   |-- ARCHITECTURE_V1.md
|   |-- SECURITY_MODEL_V1.md
|   `-- adr/
|
|-- fixtures/
|
`-- CDriveSmartClean.sln
```

This is a modular monolith, not a microservice architecture.

## 6. Dependency rule

Allowed dependency direction:

```text
Desktop
   |
   v
Application
   |
   v
Domain
```

Infrastructure implementations point inward through abstractions:

```text
Scan --------------------+
Analysis ----------------+
Platform.Windows --------+--> Application/Domain abstractions
Cleanup -----------------+
Persistence -------------+
AI ----------------------+
```

`CDriveSmartClean.Domain` must not reference infrastructure packages.

## 7. Domain layer

Project:

```text
CDriveSmartClean.Domain
```

Contains stable business concepts only, such as:

- `Finding`
- `Evidence`
- `SizeMetrics`
- `VolumeAccounting`
- `ReclaimEstimate`
- `RiskAssessment`
- `ProtectionState`
- `Confidence`
- `CleanupAction`
- `CleanupPlan`
- `UserDecision`
- `VerificationResult`
- `AiInsight`

The domain layer contains no filesystem traversal, WPF, SQLite, registry, HTTP client, PowerShell, or AI SDK dependencies.

## 8. Application layer

Project:

```text
CDriveSmartClean.Application
```

Coordinates use cases:

- `StartQuickScan`
- `StartSmartScan`
- `StartDeepScan`
- `CancelScan`
- `AnalyzeScan`
- `BuildCleanupPlan`
- `ApproveAction`
- `ExecuteCleanupPlan`
- `VerifyCleanup`
- `CompareScans`
- `RequestAiAnalysis`

This layer coordinates abstractions but does not directly mutate the filesystem.

## 9. Scan engine

Project:

```text
CDriveSmartClean.Scan
```

Responsibilities:

- discover the actual system volume;
- enumerate filesystem objects;
- preserve file/object identity where possible;
- collect logical and physical allocation data;
- aggregate directory usage;
- detect and safely handle reparse points;
- avoid crossing to unrelated volumes;
- account for hard links/shared allocation;
- stream progress/results;
- support cancellation.

Representative abstractions:

```text
IStorageScanner
IStorageEnumerator
IAllocationReader
IVolumeAccountingProvider
IObjectIdentityProvider
```

## 10. Windows platform layer

Project:

```text
CDriveSmartClean.Platform.Windows
```

Provides Windows-specific implementations for:

- volume discovery;
- NTFS/file identity;
- allocation size;
- reparse-point inspection;
- sparse/compressed attributes;
- cloud attributes;
- recycle bin;
- installed applications;
- Windows system-managed storage;
- VSS/restore information where supported;
- UAC/elevation integration;
- Windows-native cleanup capabilities.

The scan domain does not hard-code `C:\`.

## 11. Filesystem enumeration strategy

### v1 baseline

Use a robust Win32/.NET filesystem enumerator with explicit handling for:

- access denied;
- long paths;
- reparse points;
- junction loops;
- mount points;
- deleted/changed objects during scanning;
- cancellation.

### future optimization

Introduce:

```text
IFileSystemEnumerator
  |-- Win32FileSystemEnumerator
  `-- NtfsOptimizedEnumerator
```

A later NTFS-specific implementation may use more direct filesystem metadata for performance.

For incremental scans, a future `USN Journal` adapter may update only changed regions after a trusted baseline scan.

## 12. Streaming and aggregation

The UI must not wait for the entire scan to finish before showing useful information.

Pipeline:

```text
enumerator
  -> raw storage observations
  -> hierarchical aggregator
  -> live category/folder totals
  -> incremental UI updates
  -> final deterministic analysis
```

The engine should not keep one heavy in-memory domain object for every file on multi-million-file systems.

Detailed records should be retained only where needed, for example:

- large-file analysis;
- duplicate candidates;
- cleanup targets;
- anomalies;
- security-sensitive target identity.

Most other data should be hierarchically aggregated.

## 13. Analysis engine

Project:

```text
CDriveSmartClean.Analysis
```

Pipeline:

```text
Raw inventory
  -> Physical accounting
  -> Universal classification
  -> Facet analysis
  -> Application attribution
  -> Knowledge enrichment
  -> Large-object analysis
  -> Dormant analysis
  -> Orphan analysis
  -> Duplicate analysis (mode-dependent)
  -> Growth analysis
  -> Reclaim estimation
  -> Risk assessment
  -> Priority calculation
```

No specialized application module may bypass the universal inventory layer.

## 14. Application attribution

Attribution is deterministic enrichment.

Possible sources:

- installed app metadata;
- registry uninstall entries;
- MSIX/AppX metadata;
- executable metadata;
- running process/service ownership;
- known-path rules;
- filesystem signatures.

Output:

```text
ApplicationAttribution
{
    ApplicationName
    Publisher
    Confidence
    Evidence[]
}
```

Failure to attribute does not remove or hide a finding.

## 15. Knowledge layer

Project:

```text
CDriveSmartClean.Knowledge
```

Purpose:

- enrich universal findings;
- identify known cache/temp/application structures;
- provide purpose and impact descriptions;
- map known data types to existing safe action kinds.

It may include knowledge for browsers, games, office products, media tools, IDEs, package managers, container tools, cloud clients, and many other products.

The core scanner remains independent of this knowledge.

## 16. Declarative rule packs

Application-specific knowledge should prefer declarative rules over executable plugins.

A rule may describe:

- known application identity;
- known path pattern;
- category/facets;
- reproducibility;
- expected ownership;
- allowed predefined action kind.

A rule must not contain arbitrary PowerShell, CMD, shell, or executable code.

Future remote rule updates must be signed and versioned.

## 17. Duplicate engine

A dedicated component may be exposed as:

```text
CDriveSmartClean.Duplicates
```

or remain internal to Analysis v1.

Pipeline:

```text
group by logical size
  -> lightweight sample fingerprint
  -> strong hash for remaining candidates
  -> physical-allocation reconciliation
  -> duplicate groups
```

Deep hashing is primarily a Deep Scan function.

## 18. Reclaim estimation and overlap resolver

The reclaim subsystem must distinguish:

- physical allocation;
- expected reclaim;
- conditional reclaim;
- user-decision reclaim;
- unknown reclaim.

The planner must resolve overlapping findings and shared physical allocation before presenting net expected reclaim.

Representative components:

```text
ReclaimEstimator
OverlapResolver
CleanupValueCalculator
```

## 19. Cleanup subsystem

Project:

```text
CDriveSmartClean.Cleanup
```

Responsibilities:

- build cleanup plans from user selections;
- resolve overlap;
- enforce risk/protection policy;
- validate action preconditions;
- construct immutable action manifests;
- coordinate execution;
- verify actual results.

Representative components:

```text
CleanupPlanBuilder
PolicyEngine
ActionValidator
RevalidationEngine
VerificationEngine
```

The cleanup layer does not accept arbitrary shell command strings.

## 20. Action manifest

The privileged executor receives a typed manifest.

Conceptually:

```text
ActionManifest
{
    ActionId
    ActionKind

    ScanSessionId
    ApprovalId

    TargetVolumeIdentity
    TargetObjectIdentity
    ExpectedPath

    Preconditions[]
    SafetyChecks[]

    ManifestVersion
}
```

The exact security fields are defined further in `SECURITY_MODEL_V1.md`.

## 21. IPC

Desktop-to-executor communication uses local IPC, initially expected to be named pipes.

Requirements:

- local machine only;
- authenticated/validated peer identity;
- strict message schema;
- versioning;
- bounded payloads;
- per-operation correlation/nonces;
- no arbitrary serialized object activation;
- no raw command strings.

## 22. AI layer

Project:

```text
CDriveSmartClean.AI
```

Core abstraction:

```text
IStorageAnalyst
```

Possible implementations:

```text
NoAiAnalyst
CloudAiAnalyst
LocalAiAnalyst   // future
```

Input:

```text
SanitizedStorageReport
```

Output:

```text
AiAnalysisReport
```

The AI project must not reference or receive:

- `ICleanupExecutor`;
- arbitrary filesystem mutation interfaces;
- process runners;
- registry writers;
- privileged IPC actions.

The separation must be architectural, not merely prompt-based.

## 23. AI sanitization

Before cloud AI processing:

- remove or alias user identity;
- redact sensitive path segments;
- remove secrets/tokens;
- exclude file contents;
- exclude browser history/cookies/passwords;
- avoid client/project names unless the user explicitly opts in.

The AI receives metadata sufficient for storage analysis, not unrestricted personal content.

## 24. Persistence

Project:

```text
CDriveSmartClean.Persistence
```

SQLite is the baseline local store.

Persist:

- scan sessions;
- volume snapshots;
- aggregated findings;
- historical size/growth data;
- cleanup plans;
- user approvals;
- execution results;
- verification results;
- non-sensitive preferences.

Do not store user file contents.

Retention policy should be configurable.

## 25. Historical matching

Future scans match current observations to previous identities/paths where reliable.

Outputs include:

- growth delta;
- shrink delta;
- new large areas;
- disappeared areas;
- changed category/ownership.

History is used to improve attention priority, not to authorize deletion.

## 26. Desktop UI

Project:

```text
CDriveSmartClean.Desktop
```

WPF + MVVM is the v1 baseline because the product benefits from mature Windows integration, virtualization of large trees/lists, and predictable desktop behavior.

Likely screens:

- Dashboard
- Scan Progress
- Storage Overview
- Physical Tree
- Findings
- Large Files
- Duplicates
- Applications
- System
- Unknown
- Cleanup Plan
- AI Analysis
- History
- Settings

The UI framework must remain outside Domain/Application.

## 27. Main dashboard contract

The first screen after analysis should summarize:

- system volume;
- capacity/used/free;
- health state;
- percentage of used storage explained;
- physical usage by major category;
- estimated net reclaimable space;
- amount requiring user review;
- protected/non-actionable areas;
- top causes of usage/growth.

The UI must never present logical placeholder size as guaranteed reclaim.

## 28. Performance strategy

Key requirements:

- asynchronous scanning;
- cancellation;
- streaming progress;
- bounded memory;
- hierarchical aggregation;
- lazy expansion of large directory trees;
- mode-based expensive analysis;
- future incremental scan support.

No performance optimization may weaken object identity or reparse/hard-link safety.

## 29. Test architecture

Synthetic scenario fixtures should include:

```text
NormalHomePC
OfficePC
GamingPC
CloudHeavyPC
OldWindowsPC
DeveloperPC
MediaWorkstation
UnknownGrowthPC
```

Edge-case fixtures:

```text
SparseFile
HardLinks
JunctionLoop
MountPointToOtherVolume
CloudPlaceholder
LargeVhdx
DuplicateSet
Unknown100GB
ProtectedSystemPath
NestedOverlap
ChangingTargetDuringScan
```

## 30. Virtual-disk integration tests

Where practical, integration tests should use disposable VHD/VHDX volumes.

Benefits:

- isolated filesystem behavior;
- safe destructive-action testing;
- reproducible NTFS fixtures;
- reparse/hard-link scenarios;
- no risk to the developer's real system volume.

Cleanup integration tests must not operate destructively on the developer's actual system drive.

## 31. Security architecture requirements

Architecture must support:

- least privilege;
- explicit user approval;
- immutable action manifests;
- independent executor revalidation;
- path canonicalization;
- volume/object identity checks;
- reparse-point defense;
- TOCTOU mitigation;
- no arbitrary command execution;
- signed declarative rule updates;
- auditable results.

Detailed requirements are in `SECURITY_MODEL_V1.md`.

## 32. Development sequence

### Milestone 1 — Foundation

- solution/project structure;
- domain models;
- contracts;
- CI;
- baseline security tests;
- architecture enforcement.

### Milestone 2 — Universal read-only scanner

- system-volume detection;
- filesystem enumeration;
- allocation accounting;
- reparse/hard-link correctness;
- volume coverage.

### Milestone 3 — Universal analysis

- categories/facets;
- largest/old/unknown;
- attribution;
- reclaim model;
- risk/confidence.

### Milestone 4 — Smart analysis

- duplicate engine;
- orphan candidates;
- virtual/cloud handling;
- history/growth.

### Milestone 5 — Cleanup planning

- policy;
- overlap resolver;
- predefined actions;
- user approval model.

### Milestone 6 — Privileged executor

- UAC helper;
- secure IPC;
- pre-execution revalidation;
- execution;
- verification.

### Milestone 7 — Knowledge enrichment

- broaden recognized products and native cleanup mechanisms.

### Milestone 8 — AI analyst

- sanitized DTO;
- explain/correlate findings;
- no execution tools.

### Milestone 9 — Commercial delivery

- packaging;
- updater;
- code signing;
- licensing;
- Store readiness;
- website/download integration.

## 33. Architectural non-goals for v1

Do not introduce without a demonstrated need:

- microservices;
- always-running Windows service;
- kernel driver;
- filesystem filter driver;
- arbitrary executable plugins;
- AI tools capable of system mutation;
- cloud-hosted filesystem scanning.

## 34. Architecture decision baseline

Architecture v1 is accepted on these principles:

1. universal scan before specialized knowledge;
2. deterministic analysis before AI;
3. local-first;
4. non-elevated UI;
5. privilege-separated executor;
6. predefined typed actions only;
7. user approval before mutation;
8. object revalidation before execution;
9. verification after execution;
10. modular monolith with inward dependency direction.

## 35. F1-12 raw storage measurement boundary

The Windows x64 enumerator records `EndOfFile` as logical bytes and `AllocationSize` as filesystem-reported
allocation from the same `FILE_ID_EXTD_DIR_INFO` observation. Values are signed, nonnegative, point-in-time
metadata. Unavailable values remain null under an explicit availability state; zero always means a measured zero.
Directory measurements describe the directory entry, not recursive subtree usage. Reparse measurements describe
the entry, not its target. Reported allocation is not exclusive, unique, reclaimable, or reconciled volume usage;
F1-13 owns hard-link/shared-allocation accounting and aggregation.

Portable attributes are mapped through source-specific paths. Directory enumeration may report
`RecallOnOpen`; `FileAttributeTagInfo` may not, because its numerically equal `0x00040000` bit has EA semantics.
The two sources therefore never share a generic raw-bit mapper.

Before every relative descendant open, the adapter freshly enumerates the requested literal component from its
validated pinned parent handle. It requires an ordinary non-reparse directory, a native ID, and no known
recall-sensitive enumeration state. The component is then opened relative to that same parent with existing-only,
no-follow and `FILE_OPEN_NO_RECALL` options. Handle-visible attributes, identity, volume serial and GUID-volume
provenance are validated before the new handle becomes an enumeration parent. Caller-provided `StorageEntry`
metadata is data, not traversal authority.

Known recall-sensitive directories remain visible and produce explicit incomplete-coverage issues. Fresh lookup
adds native enumeration work and is deliberately uncached. The filesystem remains live: a state change between
lookup and open is possible, and `RecallOnOpen` cannot be revalidated through the approved handle query.
`FILE_OPEN_NO_RECALL` is an acquisition mitigation, not evidence of zero provider callbacks, network traffic,
metadata hydration, or provider-independent behavior. Real provider, ReFS, and cross-volume integration evidence
remain separate requirements.

---

## 36. F1-13 observed storage accounting

The accounting engine composes the unchanged tree walker with a compact per-call identity/path ledger.
It forwards every discovered entry and traversal issue. It does not open files, read content, enumerate
streams, classify findings, estimate reclaim, or populate legacy SizeMetrics/VolumeAccounting.

DeduplicatedObservedAllocatedBytes counts filesystem-reported allocation once per eligible observed
volume-bound native identity. This is observed identity aliasing, not exclusive allocation, physical
block deduplication, or device consumption. An identity seen once does not prove that no unseen links exist.
Repeated identical paths are idempotent. Contradictory path evidence invalidates every affected identity;
contradictory identity evidence invalidates that group. No first/last/min/max measurement wins.

Raw logical and reported-allocation subtotals are distinct-path diagnostics. Available but ineligible
measurements form an uncertain measured subtotal, which must not be added to deduplicated allocation.
Unavailable/not-applicable measurements have counts, not invented byte estimates. Conflicted paths are
excluded from numeric path and kind counts because their evidence is ambiguous.

The hierarchy uses a synthetic root, ordinal case-preserving root-relative paths, and no Unicode
normalization. Implicit parents are not observed directories. Every eligible identity is attributed once
to its aliases' containing directories' lowest common ancestor (LCA). Parent direct allocation plus
child inclusive allocation is additive; parent and child inclusive totals overlap. Group counts and
extra-alias counts are also attributed once at the LCA; unique counts mean non-conflicted identities.
Final collections are sorted. Finalization uses an ancestor index and iterative bottom-up reduction,
not a recursive or per-file ancestor-summing walk.

Accounting quality is Complete, Incomplete, Inconsistent, or Unavailable, with combinable reasons.
Complete means processing of the supported evidence model, not complete physical-volume coverage.
All arithmetic is checked. Identity/path/directory limits and a deterministic conservative estimated
state budget guard accounting allocations; this budget is not an exact managed-heap measurement.
Defaults are 1,000,000 identities, 1,000,000 paths, 100,000 hierarchy nodes including the synthetic root,
and 512 MiB estimated state. Charges include retained names, collection overhead and finalization buffers.
ResourceLimit or ArithmeticOverflow releases authoritative state and produces null accounting totals
and no prefix hierarchy/groups. Safe traversal, entry forwarding and issue forwarding continue.
Security violations, cancellation and downstream failures still propagate normally.

WindowsVolumeSpaceProvider authoritatively rediscovers the system volume, checks the caller descriptor,
then queries its volume GUID root through GetDiskSpaceInformationW. This read-only API requires Windows
10 build 17763 / Server 2019 or later. Unsupported APIs return an explicit unavailable snapshot; there is
no drive-letter or GetDiskFreeSpaceEx fallback. Actual total/available allocation units produce capacity/free;
used is capacity minus free. Caller quota values and native used/reserve diagnostics remain separate.
Unsigned products must fit Int64. Reserve diagnostics are not disjoint accounting buckets.

Start/end snapshots are live samples. Equal samples do not prove an atomic scan. A nonzero used-space
delta makes reconciliation inconsistent and coverage unavailable. The signed residual is end used minus
eligible observed allocation; negative values remain negative. Positive residuals are unattributed,
never automatically filesystem-reserved. Stable observed coverage is eligible allocation / sampled used,
with explicit incomplete quality for evidence gaps. It is never clamped to 100%. Empty-volume coverage
requires completed, available, gap-free accounting; unavailable samples never become zero snapshots.

Directory entry allocation (including omitted root metadata), reparse entry allocation, and known provider
attributes Offline/RecallOnOpen/RecallOnDataAccess/Pinned/Unpinned are excluded from the numerator while
raw evidence stays visible. Ordinary sparse/compressed files retain their exact reported allocation.
No cloud hydration is added. ADS completeness, distinct-identity block sharing, NTFS Dedup, ReFS cloning,
WOF internals, and filesystem-reserved attribution remain unsupported. Real cloud-provider, ReFS,
cross-volume, native ARM64 and native x86 integration remain unverified.

---

## 37. F1-14 deterministic universal analysis

`CDriveSmartClean.Analysis` consumes the same validated `StorageEntry` stream forwarded by
`StorageAccountingEngine`. A per-scan `IStorageAnalysisSession` captures compact path and identity facts and
joins them with the completed F1-13 result. Analysis never rescans, reopens a path, reads content, follows a
reparse target, or becomes a filesystem authority. Domain remains independent; Analysis references Application
and Domain and has no Windows dependency.

F1-14 produces an immutable staged `StorageAnalysisResult`, not final `Finding` objects.
`Finding`, `SizeMetrics`, `ReclaimEstimate`, `RiskAssessment`, and protection policy remain deferred.
In particular, analysis does not invent exclusive allocation, reclaimable bytes, risk, cleanup permission, or
zero values for unavailable measurements.

Primary classification uses fixed deterministic rules with explicit authority and specificity. Equal strongest
rules that disagree produce `Unknown`; rule registration and scan order never select a winner. Implemented
primary categories are Unknown, System, Application, ApplicationData, and UserData. Temporary, Cache,
LogDiagnostic, InstallerArchive, BackupSnapshot, VirtualStorage, CloudBacked, RecycleBin, and
FilesystemOverhead remain deferred. Implemented entry-derived facets are Compressed, Sparse, CloudPlaceholder,
and observed-identity HardLinked. Large, timestamp, growth, duplicate, orphan, ownership, active/protected, and
purpose-like facets remain deferred. Unknown is a valid complete classification and is distinct from unattributed
volume residual.

Every non-Unknown rule emits a stable evidence code, explanation, and confidence. Verified is reserved for
authoritative OS/native facts; strong purpose classification from accepted platform roots is High. Generic
recall-sensitive attributes can establish CloudPlaceholder but not cloud-provider ownership or the CloudBacked
primary category. Observed hard-link evidence means only that multiple visible paths share one native identity;
it does not prove the global link count or exclusive allocation.

Category accounting preserves three independent quantities: raw reported path allocation, uncertain measured
allocation, and deduplicated observed allocation. Eligible identities contribute physical allocation exactly once.
Aliases agreeing on category use that category; cross-category aliases contribute once to Unknown, never to the
first observed alias and never to both categories. Identity-unavailable or unsupported evidence stays raw and
uncertain. Conflicted paths contribute no ambiguous numeric path totals. Complete authoritative category sums
must exactly equal the corresponding F1-13 raw, uncertain, and deduplicated totals. A mismatch produces
`AnalysisQuality.Unavailable` with `AccountingMismatch`; no balancing Unknown bytes are invented.
Volume reconciliation residual remains separate.

The join to caller-constructible F1-13 results is bidirectional. Analysis retains bounded identity-to-path and
path-to-identity associations, reconstructs group eligibility and conflict reasons from the observed stream, and
requires every allocation group's identity, complete path set, reasons, and eligible reported allocation to agree.
Swapped, missing, extra, stale, or tampered groups therefore become `AccountingMismatch`, including legitimate
poisoned paths that observed more than one identity without reducing validation to the first observed fact.

Largest hierarchy candidates rank non-synthetic hierarchy nodes by inclusive attributed observed allocation.
Largest identity candidates rank eligible groups, and the file view includes only eligible single-path file
groups. Multi-alias groups are not multiplied into physical file candidates. Unknown candidates are a filtered
listing view, not an additive total. Each view uses a bounded deterministic top-N set (default 100, maximum
1,000) with allocation, scope, ordinal path, volume identity, and object identity tie-breaking. Ranking does not
add the Large facet.

Application exposes a platform-neutral, volume-bound `StorageClassificationContext`. The Windows provider
rediscovers the trusted system volume, obtains the mandatory Windows directory from
`GetSystemWindowsDirectoryW`, and uses `SHGetKnownFolderPath` with `KF_FLAG_DONT_VERIFY` for Program Files,
ProgramData, current-user Profile/AppData, Public, and UserProfiles roots. Existing
`GetVolumePathNameW`/`GetVolumeNameForVolumeMountPointW` declarations bind every accepted root to the same
GUID volume. Optional missing or foreign-volume roots are omitted and make context incomplete; no environment,
drive-letter, localized-name, registry, or filesystem fallback exists. The only new native declaration is
`SHGetKnownFolderPath`, and its CoTaskMem result is always released.

`StorageClassificationContext` is authoritative input, not a filesystem proof created merely by attaching a
`VolumeIdentity`. Production callers must obtain it from a trusted `IStorageClassificationContextProvider`;
provider implementations own root provenance and same-volume binding. Application contracts reject
drive-relative, malformed GUID-volume, ADS, mixed-separator, NUL, empty-component, and dot-segment roots.
Analysis deliberately trusts the supplied context and performs no filesystem/native provenance revalidation,
rescan, or path reopen.

Analysis uses checked arithmetic, path/identity limits, bounded candidate sets, and a deterministic estimated
256 MiB state budget. Resource exhaustion or arithmetic overflow clears F1-14 authoritative state and marks
analysis unavailable, but the sink continues accepting safe entries so successful F1-13 scanning/accounting can
finish. Volume validation and cancellation remain active after degradation. Cancellation propagates and never
becomes a completed degraded result.

Hierarchy candidate generation uses an explicit iterative traversal; filesystem depth does not consume managed
call-stack frames. Candidate evidence codes are semantic identities: exact duplicates are collapsed, while a
same-code difference in description or confidence is rejected instead of selecting an arbitrary winner.

---

## 38. F1-15 universal findings

`UniversalFindingBuilder` is the final deterministic Domain-finding stage over the immutable F1-14 analysis and
F1-13 accounting results. It creates `Finding` objects without rescanning, reopening paths, reading or hashing
content, invoking native APIs, or creating cleanup actions. The stage remains useful with zero recognized
products: System, Application, ApplicationData, UserData, and Unknown category views, significant hierarchy,
identity and file views, generic cache-like areas, and physically large objects remain explainable without
vendor rules or application ownership.

`SizeMetrics` values are nullable and independently truthful: null is unavailable and zero is known zero.
Category allocation uses deduplicated observed allocation. Object candidates are joined back to exact F1-13
allocation groups or hierarchy aggregates before publication; identity, paths, allocation, counts, category, and
classification confidence must agree. File and identity-group logical bytes are withheld because accounting has no
independent exact logical fact for those scopes, while hierarchy logical bytes are retained only after exact aggregate
validation. Observed attributed allocation is never promoted to exclusive allocation, so every F1-15 finding has
`ExclusiveAllocatedBytes = null`. Logical and allocated values never become reclaim values. Every universal
finding uses `ReclaimEstimate.Unknown("finding.reclaim.unknown")` with no numeric bounds.
Candidate classification reuses the unchanged F1-14 classification engine after lexically normalizing trusted context
roots into the system-volume root representation. If accounting provides no authoritative deduplicated allocation,
every category deduplicated value must also be null; analysis-side path, raw, and uncertain evidence may remain.

Finding scopes are CategoryAggregate, HierarchyArea, IdentityGroup, and File. Their path, identity, and count
invariants are enforced by Domain. Evidence is ordinally code-sorted; identical same-code evidence is collapsed
and semantic collisions are rejected. The `finding.` evidence namespace is owned by the final-Finding builder and is
invalid in upstream staged candidates. Scan-local finding IDs are UUIDv5 values derived from the scan session and
versioned semantic keys, so enumeration order cannot change identity or output ordering.

Risk means the risk of treating the finding as a destructive cleanup target without further deterministic
enrichment, action validation, and user approval; it is not risk caused by the object's existence. System is
Critical and Protected. Application, ApplicationData, UserData, Unknown, and cache-like ApplicationData are
High and ReviewRequired. Risk-policy confidence is Verified and remains separate from finding confidence and
the Unknown reclaim confidence. Finding confidence begins with category/candidate confidence, takes the minimum
of material facet confidences, and is capped at Medium for incomplete output.

Large is only a physical-significance signal. Its threshold is
`clamp(EndSnapshot.CapacityBytes / 100, 1 GiB, 16 GiB)` and compares observed attributed allocation, never
logical size. If capacity is unavailable, Large is omitted and output is incomplete. CacheLike is a Medium-
confidence ApplicationData heuristic only when an exact `cache` or `caches` descendant component occurs below
trusted current-user AppData or ProgramData roots. Matching uses canonical volume-relative components, so equivalent
drive and GUID-volume root spellings do not change the result; it never means safe, reproducible, or owned by a known app.
Old/Recent, product knowledge, duplicate/orphan inference, and cleanup actions remain unimplemented.

Category findings are reserved first; each object/cache view validates and merges every same-key draft through one
shared semantic map before deterministic bounded top-K retention, so a cache contradiction cannot disappear through
set equality or truncation. The
accounting hierarchy is indexed iteratively under the resource limit and the same bounded index feeds cache matching.
Cross-result volume,
quality, reason, total, identity, and duplicate-key contradictions yield empty Unavailable output. Category,
hierarchy, identity, and file sizes are overlapping explanatory views and must never be summed into net reclaim.

---

## 39. MVP-01 Windows product runtime

`CDriveSmartClean.Runtime` is the Windows product composition root. It targets `net10.0-windows`/x64 and
references Application, Scan, Analysis, and Platform.Windows; none of those projects references Runtime. A future
Desktop project depends on Runtime. Runtime has no hosting, delivery, persistence, telemetry, cleanup, or other
infrastructure dependency.

`SystemVolumeScanWorkflow` composes the existing Windows providers, traversal/accounting engine, analyzer, and
finding builder. One invocation discovers the trusted system volume and classification context, creates one analysis
session, then creates exactly one `StorageTreeWalker`. `StorageAccountingEngine` accounts each validated entry and
forwards that same `StorageEntry` exactly once through a progress sink to the analysis session. Analysis completes
only after accounting has produced its final result; universal findings are built only after analysis completes. The
workflow never rescans, buffers the complete entry stream, reopens a path, reads content, or mutates the filesystem.

The immutable `ProductScanResult` carries the system-volume identity, start/end snapshots, authoritative accounting,
reconciliation, analysis, and finding qualities/reasons, traversal completion, a defensive read-only copy of
`StorageAccountingResult.IssueCounts`, and a defensive read-only copy of the complete F1-15 findings. Runtime does
not reinterpret sizes, reclaim, risk, confidence, protection, evidence, residual, coverage, or unavailable states.
Incomplete usable results remain results; unavailable finding output remains explicitly unavailable and empty.

Progress reports only the ordered phase, objects observed, raw reported allocation observed, and traversal issues
observed. Counters never decrease. Raw reported allocation is observation progress, not deduplicated, exclusive,
reclaimable, or volume-used space. No percentage, ETA, object total, or remaining-byte estimate is exposed. The
issue progress sink retains no issue paths or objects; final issue counts come only from accounting.

The same cancellation token flows through accounting, forwarding sinks, analysis completion, and finding
construction. Cancellation remains authoritative until `Completed` publication begins. Once `Completed` is
successfully reported synchronously, cancellation requested by that observer does not retroactively cancel the
completed result. Exceptions thrown directly and synchronously by `IProgress<ProductScanProgress>.Report` propagate
through `ScanAsync`; failures from asynchronously dispatched callbacks occur outside the workflow call boundary and
cannot be propagated by `ScanAsync`. Unexpected provider and programming failures also propagate. `IStorageScanner`
remains legacy/unused for this product workflow and is neither implemented nor removed.

This document is the technical baseline for implementation and must remain consistent with `BUSINESS_LOGIC_V1_1.md` and `SECURITY_MODEL_V1.md`.
