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

---

This document is the technical baseline for implementation and must remain consistent with `BUSINESS_LOGIC_V1_1.md` and `SECURITY_MODEL_V1.md`.
