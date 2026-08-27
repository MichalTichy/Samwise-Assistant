# Shared.Infrastructure.LeaderElection

This project defines the leader-election seam used by startup initializers. Samwise deploys as one process, so the default implementation always treats the current instance as leader.

## Main components

- `ILeaderElection` defines leader checks, readiness waiting, and re-election initialization.
- `LeaderElectionSingleInstance` is the V1 single-process implementation.
- `LeaderElectionInstaller` registers the single-instance fallback at low priority.

## Initialization flow

1. `InitializerManager` asks whether this process is the leader.
2. `LeaderElectionSingleInstance` immediately returns `true` and ready.
3. All selected startup initializers run locally.

## Boundary

There is no distributed lock, lease, cluster membership, or failover logic here. A real leader-election implementation should be added only if Samwise later runs more than one web process.
