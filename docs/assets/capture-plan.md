# Documentation capture plan

- **Purpose:** capture the real Argoscope portfolio dashboard for README documentation.
- **Source:** local app route http://127.0.0.1:<port>/ using synthetic repository observations.
- **Command/settings:** shot-scraper http://127.0.0.1:<port>/ --output docs/assets/README.jpg --width 1440 --height 900 --wait 1500 --quality 85.
- **Artifact:** docs/assets/README.jpg (not created; no application surface exists).
- **Revision/time:** bootstrap revision is uncommitted; capture timestamp pending implementation.
- **Privacy review:** pending. Use synthetic repo names and metric data; remove tokens, private repository identity and owner-only traffic before committing.
- **Status:** BLOCKED. No Argoscope app is implemented or running, so no genuine screenshot can be captured. Next action: implement the active OpenSpec change, run the app, capture the real route, and add revision/time/privacy review evidence.
