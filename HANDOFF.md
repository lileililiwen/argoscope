current_spec: github-portfolio-momentum-mvp

# Handoff

## State

Repository bootstrap and planning artifacts are in progress. This is **PLANNING ONLY**: no API, UI, database schema, GitHub integration, snapshot job, build, or runtime has been implemented or verified. Git repository initialized on main; initial publication and GitHub metadata still need verification.

## Active change

openspec/changes/github-portfolio-momentum-mvp/ is the first implementation package, ordered by ROADMAP.md.

## Verification evidence

- openspec init --tools codex created repository OpenSpec structure (spec-driven). Codex prompt installation was blocked because the user-level prompt directory is read-only; repository OpenSpec files are available.
- .NET SDK 10.0.400 is installed. No Argoscope solution or web app exists yet.
- GitHub CLI authentication passed outside the sandbox. Initial repository existence check found no prior lileililiwen/argoscope.
- Published with gh repo create lileililiwen/argoscope --public --source . --remote origin --push; initial commit f0886092d42d67bab6552c84ce03c66cb7a1c2ad reached main.
- gh repo edit set the approved description, homepage https://github.com/lileililiwen/argoscope, and topics github-analytics, open-source, portfolio-analytics, repository-metrics.
- The shared Workspace Governance GitHub metadata publisher returned metadata_verified with no differences and recorded publication in .project.json.
- gh repo view --json nameWithOwner,description,homepageUrl,visibility,repositoryTopics,url reported public lileililiwen/argoscope and matching description/homepage/topics. git ls-remote --heads origin main returned f0886092d42d67bab6552c84ce03c66cb7a1c2ad refs/heads/main.
- OpenSpec strict validation passed (1 change, 0 failures). Workspace Governance reported DISCOVERED_UNREGISTERED and CI_MISSING; it also reported the unrelated pre-existing missing directory jenkins-bootstrap. Central registry changes are outside this bootstrap.
- No application build, tests, live GitHub API request, runtime, or screenshot has been run. Capture is blocked because there is no application source tree; docs/assets/capture-plan.md records the next action. CI awaits an executable application source tree.

## Next actions

1. Finish and strictly validate the active OpenSpec package.
2. Implement only this change; collect and verify live API evidence before claiming GitHub integration.
3. Run the application with synthetic repository data and capture a privacy-reviewed screenshot.
4. Create/push the public GitHub repository, apply description/homepage/topics, then verify gh repo view and git ls-remote.

Each completed OpenSpec spec/change requires exactly two commits: first the implementation/tests/archive commit, then a HANDOFF.md-only pointer/evidence commit.
