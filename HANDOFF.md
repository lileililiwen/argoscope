current_spec: github-portfolio-momentum-mvp

# Handoff

## State

Repository bootstrap and planning artifacts are in progress. This is **PLANNING ONLY**: no API, UI, database schema, GitHub integration, snapshot job, build, or runtime has been implemented or verified. Git repository initialized on main; initial publication and GitHub metadata still need verification.

## Active change

openspec/changes/github-portfolio-momentum-mvp/ is the first implementation package, ordered by ROADMAP.md.

## Verification evidence

- openspec init --tools codex created repository OpenSpec structure (spec-driven). Codex prompt installation was blocked because /home/paul/.codex/prompts/opsx-explore.md is read-only; repository OpenSpec files are available.
- .NET SDK 10.0.400 is installed. No Argoscope solution or web app exists yet.
- GitHub CLI authentication was confirmed outside the sandbox. Repo existence checks found no lileililiwen/argoscope repository.
- No application build, tests, live GitHub API request, runtime, or screenshot has been run. docs/assets/capture-plan.md records the capture blocker and retry steps.
- Workspace Governance audit reported DISCOVERED_UNREGISTERED, CI_MISSING, and PUBLICATION_UNVERIFIED for this new repository; it also reported the unrelated pre-existing missing directory jenkins-bootstrap. Central registry changes are outside this bootstrap. Publication evidence will be updated after GitHub creation; CI awaits an executable application source tree.

## Next actions

1. Finish and strictly validate the active OpenSpec package.
2. Implement only this change; collect and verify live API evidence before claiming GitHub integration.
3. Run the application with synthetic repository data and capture a privacy-reviewed screenshot.
4. Create/push the public GitHub repository, apply description/homepage/topics, then verify gh repo view and git ls-remote.

Each completed OpenSpec spec/change requires exactly two commits: first the implementation/tests/archive commit, then a HANDOFF.md-only pointer/evidence commit.
