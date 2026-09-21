# CDriveSmartClean — Security Model v1

**Status:** Baseline  
**Version:** 1.0  
**Date:** 2026-09-22  
**Depends on:** `docs/BUSINESS_LOGIC_V1_1.md`, `docs/ARCHITECTURE_V1.md`

## 1. Security objective

CDriveSmartClean analyzes and may delete or alter data on the Windows system volume after explicit user approval. A defect or compromise can therefore cause significant data loss or system damage.

The primary security objective is:

> **No data-changing operation occurs unless it is a known product action, explicitly approved by the user, permitted by policy, independently revalidated at execution time, and verified afterward.**

AI output is never an authorization source.

## 2. Core security principles

1. Least privilege.
2. Read-only by default.
3. Non-elevated desktop process.
4. Elevation only for specific approved actions.
5. No arbitrary shell-command execution.
6. No arbitrary PowerShell execution.
7. No executable third-party cleanup plugins in v1.
8. Explicit user approval before mutation.
9. Immutable/typed cleanup action manifests.
10. Independent executor validation.
11. Target identity validation, not path-string trust alone.
12. Reparse/junction/mount-point defenses.
13. TOCTOU mitigation.
14. Fail closed on ambiguity.
15. AI has no mutation capability.
16. Cloud AI receives sanitized metadata only.
17. Signed software/rule updates.
18. Auditable cleanup decisions and results.
19. Safe defaults for user/system data.
20. Security tests are release gates, not optional tests.

## 3. Assets to protect

### User data

- documents;
- photos/media;
- downloads;
- archives;
- project/source trees;
- application data;
- databases;
- cloud-backed local data;
- browser/profile data.

### System integrity

- Windows installation;
- boot/system files;
- registry/system configuration;
- Windows Installer data;
- update/component stores;
- system services;
- restore/recovery mechanisms.

### Application integrity

- installed applications;
- application configuration;
- licenses/tokens;
- virtual environments;
- virtual machines/containers;
- application databases.

### Product trust

- CDriveSmartClean binaries;
- cleanup rules;
- updater;
- signatures;
- local database/history;
- audit trail;
- AI boundary.

## 4. Trust boundaries

Major trust boundaries:

```text
User
  |
  v
Desktop UI (non-elevated)
  |
  +--> Analysis engine (read-only)
  |
  +--> AI boundary (optional external service)
  |
  v
Cleanup plan + user approval
  |
  v
Local IPC boundary
  |
  v
Privileged Executor (elevated)
  |
  v
Windows/filesystem
```

Additional trust boundaries exist around:

- downloaded rule packs;
- software updates;
- SQLite/local persisted state;
- external/cloud AI providers.

## 5. Threat model

The security model must address at least:

- accidental deletion due to misclassification;
- arbitrary command injection;
- malicious or corrupted rule pack;
- compromised AI response;
- path traversal;
- junction/reparse redirection;
- mount-point crossing;
- symlink-style target substitution;
- TOCTOU between scan and execution;
- hard-link/shared-allocation mistakes;
- privilege escalation through executor IPC;
- forged approval/action manifests;
- stale cleanup plans;
- target changes after approval;
- over-broad administrator privileges;
- sensitive path/file metadata leakage to AI;
- local database tampering;
- malicious update/package supply chain;
- log leakage of secrets or personal paths;
- unsafe permanent deletion;
- numeric/overlap bugs that misstate reclaimable size.

## 6. Read-only scan boundary

The scan and analysis pipeline must not change disk content.

A read-only scan may:

- enumerate;
- read metadata;
- inspect attributes;
- hash duplicate candidates;
- query application/system metadata;
- query Windows storage APIs.

It must not:

- delete;
- move;
- truncate;
- dehydrate cloud data;
- uninstall software;
- stop services;
- mutate registry;
- compact virtual storage;
- clean caches;
- change Windows settings.

Read-only behavior should be testable as a product invariant.

## 7. Privilege separation

The desktop application runs non-elevated.

A separate privileged executor is launched only when a selected action requires administrator privileges.

The executor should:

- contain minimal code;
- expose a narrow typed API;
- reject unknown action kinds;
- reject malformed/stale manifests;
- revalidate targets independently;
- terminate when work is complete where practical.

The desktop application must not simply relaunch the full UI as administrator for ordinary use.

## 8. No arbitrary command execution

The product architecture must not expose a cleanup API equivalent to:

```text
RunCommand(string)
RunPowerShell(string)
RunCmd(string)
ExecuteScript(string)
```

AI, rule packs, UI, and persisted cleanup plans must never supply raw command lines to the privileged executor.

Execution is based on a finite, versioned set of typed `ActionKind` values implemented in product code.

## 9. Cleanup action authorization

A mutating action is valid only if all conditions hold:

1. deterministic logic created the action;
2. policy permits the action type for the finding;
3. the user explicitly approved the action;
4. approval references the same scan/action identity;
5. the action has not expired or become stale;
6. target identity passes revalidation;
7. current protection/risk policy still permits execution;
8. the executor recognizes the action kind and manifest version.

AI output cannot satisfy any of these conditions.

## 10. User approval proof

The system should persist a local approval record containing at minimum:

```text
ApprovalId
ScanSessionId
ActionId
ActionKind
ImpactSummaryVersion
RiskShown
ApprovedAt
```

The executor receives only the minimum proof/reference required to bind execution to an approved action.

Approval must not be reusable for a different target.

## 11. Immutable action manifest

Conceptual manifest:

```text
ActionManifest
{
    ManifestVersion

    ScanSessionId
    ActionId
    ApprovalId
    ActionKind

    TargetVolumeIdentity
    TargetObjectIdentity
    ExpectedCanonicalPath

    Preconditions[]
    SafetyChecks[]

    CreatedAt
    ExpiresAt

    CorrelationId
    Nonce
}
```

The actual serialization must be versioned and strictly validated.

## 12. Object identity

Path strings are not sufficient security identity.

Where Windows/filesystem support permits, revalidation should use a combination such as:

- volume identity/serial;
- file ID/object ID;
- canonical path;
- expected object type;
- expected reparse state;
- expected ownership/attributes;
- expected parent identity where relevant.

If the object cannot be safely proven to be the originally approved target, destructive execution fails closed.

## 13. Reparse-point and junction defense

Attack/failure scenario:

1. scan sees `C:\SomeCache`;
2. user approves cleanup;
3. before execution the path is replaced by a junction to a protected directory;
4. naive recursive delete destroys unrelated data.

Required defenses:

- inspect the target and each relevant path component at execution;
- detect reparse points;
- verify final target volume;
- do not follow unexpected reparse targets;
- compare current object identity with approved identity;
- reject target changes;
- never recursively traverse across an unapproved mount/volume boundary.

## 14. TOCTOU mitigation

Time-of-check/time-of-use risk exists between:

- scan;
- plan creation;
- user approval;
- elevated execution.

Immediately before each destructive action, the executor must repeat safety checks.

If the target changed materially, return:

```text
Blocked: target changed; re-analysis required.
```

Do not attempt to "best guess" the new target.

## 15. Protected and blocked data

`ProtectionState` is enforced centrally.

Examples of areas that may be blocked from direct deletion include critical Windows system/component/installer locations.

A finding may still:

- be scanned;
- be measured;
- be explained;
- have a supported native maintenance action where appropriate.

It must not receive a generic recursive delete action merely because it is large.

## 16. User-data safety

User-content findings default to review-required.

For ordinary user files, when deletion is selected and technically supported, prefer Recycle Bin over immediate permanent deletion.

Permanent deletion must:

- be a separate explicit action;
- display irreversible impact;
- not be the default cleanup behavior.

Age, size, duplicate status, or AI recommendation alone never authorizes removal.

## 17. Overlap and shared-allocation safety

The reclaim engine must not double-count nested/overlapping findings.

Hard-linked/shared allocations require special handling.

Security relevance:

- misleading "free 100 GB" claims can pressure the user into unsafe choices;
- deleting one hard-link instance may reclaim no physical space;
- deleting the wrong member of a duplicate group may remove the semantically important copy.

The UI must present net reclaimable estimates and uncertainty.

## 18. Cloud-placeholder safety

Cloud-backed data requires explicit distinction between:

- deleting the cloud item;
- freeing the local copy/dehydrating;
- removing a placeholder;
- removing synchronization state.

The product must not translate "free local space" into "delete remote user data."

Cloud placeholder logical size must not be treated as physical reclaim.

## 19. Virtual-storage safety

VHD/VHDX/VM/container/database stores can contain valuable nested data.

The product must not directly delete a virtual-storage container solely because it is large.

Supported cleanup/compaction must use a purpose-specific action with explicit preconditions.

Unknown virtual storage defaults to review-required/high risk.

## 20. Secure IPC

Desktop-to-executor IPC must be local-only and authenticated/restricted.

Requirements:

- deny remote clients;
- validate caller identity;
- restrict pipe/object ACLs;
- use strict schema validation;
- include protocol version;
- use bounded payload sizes;
- use correlation IDs/nonces;
- reject replay/stale requests;
- avoid unsafe general-purpose object deserialization;
- return structured status/error codes.

The privileged executor does not trust the desktop process merely because it is local.

## 21. Rule-pack security

v1 rule packs are declarative.

They may describe:

- path/signature patterns;
- classification;
- evidence;
- application ownership;
- predefined action kind mapping.

They must not embed:

- PowerShell;
- JavaScript;
- arbitrary binaries;
- DLL plugins;
- raw command lines.

Future downloadable rule packs require:

- publisher signature;
- package integrity hash;
- versioning;
- compatibility metadata;
- rollback capability;
- fail-closed verification.

Unsigned or invalid rules are ignored.

## 22. AI isolation

AI is an advisory subsystem only.

AI must not receive references/interfaces capable of:

- filesystem mutation;
- process launch;
- registry mutation;
- privileged IPC execution;
- cleanup-plan approval;
- policy override.

AI may reference only deterministic `FindingId` and pre-existing `ActionId` values in its explanation.

Any text such as "delete X" is treated as untrusted natural-language analysis, not an executable instruction.

## 23. AI privacy

Cloud AI receives sanitized structured metadata.

By default do not transmit:

- document contents;
- source-code contents;
- photos/video/audio;
- passwords;
- tokens/API keys;
- cookies;
- browser history;
- database contents;
- sensitive raw file names when unnecessary.

Sanitization should map sensitive path segments to stable aliases so the AI can correlate findings without learning personal names.

Users must be able to use the product without enabling AI.

## 24. Local data protection

SQLite/history data should contain metadata only as needed.

Avoid storing:

- file contents;
- secrets;
- raw authentication tokens;
- unnecessary sensitive filenames.

Where sensitive metadata is stored, follow OS-user access protections and least-data retention.

The application must provide a way to clear its local history/settings.

## 25. Logging and audit trail

Maintain a local audit record for mutations.

Example:

```text
Timestamp
ScanSessionId
ActionId
ActionKind
Target alias/identity
Risk shown
User approved
Expected reclaim
Execution result
Actual reclaim
Verification status
```

Security logs must avoid secrets and minimize personal path exposure.

Debug logging must not silently become a privacy leak.

## 26. Update and supply-chain security

Before public distribution:

- sign release binaries;
- publish/verify package integrity;
- use HTTPS for updates;
- verify update signatures before installation;
- pin update channel metadata to trusted signing identity;
- support safe rollback where practical;
- protect release credentials;
- generate dependency inventory/SBOM;
- scan dependencies and artifacts in CI.

The updater must never execute unsigned downloaded content.

## 27. Code-signing boundary

The following should be signed for production:

- desktop application;
- privileged executor;
- installer/package;
- updater components;
- downloadable first-party rule packs where used.

The executor may optionally verify that requests originate from the expected signed product installation in addition to OS identity/ACL checks.

## 28. Secrets

No long-lived service secrets belong in the client repository or binary.

If commercial licensing or cloud AI later requires credentials:

- user/service tokens are scoped and revocable;
- provider master keys are never shipped in the desktop binary;
- secrets are stored using Windows-supported secure credential mechanisms where applicable;
- logs never include full tokens.

## 29. Cleanup verification security

Execution success is not assumed from exit status alone.

Verify:

- target action outcome;
- allocated-size change where measurable;
- free-space delta;
- unexpected side effects/signals;
- action-specific postconditions.

A mismatch is reported, not hidden.

## 30. Failure policy

For mutating actions:

> **Ambiguity = no execution.**

Examples that must fail closed:

- target disappeared/reappeared with different identity;
- unexpected reparse point;
- changed volume;
- policy state changed;
- unknown action version;
- malformed manifest;
- approval mismatch;
- stale/expired action;
- executor cannot verify target.

## 31. Recovery strategy

Prefer reversible actions where possible.

Examples:

- user files -> Recycle Bin by default;
- application removal -> native uninstaller;
- cloud cleanup -> dehydrate/free local copy instead of remote delete where intended;
- system cleanup -> supported Windows maintenance API/tooling.

Every action should declare whether it is reversible and what recovery path exists.

## 32. Testing requirements

Security tests must cover at minimum:

- junction replacement after scan;
- reparse loop;
- mount point to another volume;
- path canonicalization edge cases;
- case/Unicode path behavior;
- target deletion/recreation between approval and execution;
- file ID mismatch;
- hard-link accounting;
- nested overlap;
- stale approval;
- forged action ID;
- unknown action kind;
- malformed IPC payload;
- replayed manifest/nonce;
- non-admin caller behavior;
- protected-path attempt;
- cloud placeholder behavior;
- virtual-disk direct-delete denial;
- AI output attempting to request execution;
- malicious/unsigned rule pack;
- action verification mismatch.

Destructive security tests should execute only against disposable fixtures/VHDX volumes.

## 33. CI/release security gates

Before a release candidate is accepted:

1. unit tests pass;
2. integration tests pass;
3. security tests pass;
4. no arbitrary-command API exists in cleanup/executor boundary;
5. dependency vulnerability scan passes defined severity policy;
6. secret scan passes;
7. binary/package signing is verified for production release;
8. rule-pack signature validation passes;
9. SBOM is generated;
10. executor privilege-boundary tests pass.

Higher-risk findings must block release rather than be silently waived.

## 34. Telemetry

Telemetry is not required for core functionality.

If introduced later:

- explicit privacy documentation;
- data minimization;
- no file contents;
- no secrets;
- avoid raw personal paths;
- separate product analytics from cleanup audit history;
- provide user controls consistent with applicable requirements.

The safety engine must not depend on telemetry availability.

## 35. Security non-goals for v1

Avoid adding attack surface without a concrete requirement:

- kernel driver;
- filesystem filter driver;
- remote administration;
- arbitrary scripting;
- third-party binary plugins;
- autonomous AI agent tools;
- always-running privileged Windows service;
- direct cloud filesystem upload for scanning.

## 36. Security acceptance criteria v1

Security Model v1 is satisfied when:

1. normal analysis works without administrator elevation;
2. mutation requires explicit user approval;
3. privileged execution is isolated from the desktop process;
4. the executor accepts typed known actions only;
5. arbitrary shell/PowerShell commands cannot cross the execution boundary;
6. target identity is revalidated at execution time;
7. unexpected reparse/mount/volume changes block execution;
8. protected findings cannot receive generic direct-delete execution;
9. AI cannot authorize or execute actions;
10. unsigned executable rule/plugin content is not supported;
11. cloud AI receives sanitized metadata only;
12. action results are verified and locally auditable;
13. destructive integration/security tests use disposable storage fixtures;
14. release CI includes security gates.

---

This security model is mandatory for implementation. Any architecture or feature that violates these invariants requires an explicit Architecture Decision Record and security review before adoption.
