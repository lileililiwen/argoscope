# Argoscope agent instructions

- Read HANDOFF.md, then the active OpenSpec change before work.
- Keep repository identity, owner/competitor role, public/private access, metric timestamp and collection status explicit.
- Use OpenSpec for non-trivial work and BFS → DFS → BFS.
- Compute velocities/rankings from persisted snapshots; show inputs, missing values and freshness. Owner activity is not external engagement.
- Follow .ai-rules/workflow.md and .ai-rules/completion.md.
- Work on main; do not create branches/worktrees unless explicitly designated.
- Evaluate dotnet-platform-libs before adding local web, persistence, or job infrastructure; do not edit the sibling here.
- Capture-plan and milestone-refresh rules live in docs/assets/capture-plan.md and the workflow.
- MIT license and source-backed GitHub metadata are in LICENSE and .project.json; GitHub publication and metadata verification are required for initialization.
- Build/test/skeleton success alone is not DONE. Blocking Gate failures or unresolved REVIEW_REQUIRED prevent completion.
- Each completed OpenSpec spec/change requires exactly two commits: first the implementation/tests/archive commit, then a HANDOFF.md-only pointer/evidence commit.
