# Memory Bloom contribution workflow

Apply this workflow to every user-requested feature or design change in this project.

Keep the documentation audiences separate:

- `README.md` is the concise user- and portfolio-facing landing page. Show only the strongest representative compact and expanded images; do not turn it into an iteration log.
- `CHANGELOG.md` is the terse release summary. Keep each version to a few user-visible outcome bullets; do not embed screenshots, root-cause narratives, implementation challenges, or per-version audit links.
- `docs/HISTORY.md` is the audit index and current/next-state overview. Link every checkpoint from its version table to the authoritative detailed `docs/history/<version>.md` record.
- `docs/history/<version>.md` is the exhaustive record for that checkpoint: requests, reported bugs, last/resulting/next states, challenges, decisions, limitations, release artifacts, and every retained screenshot (including user-supplied problem evidence).

1. Make the scoped change without overwriting unrelated user work.
2. Increase the application version for a functional checkpoint. A documentation-only reorganization does not require a new executable version or duplicate screenshots.
3. Build and verify with zero warnings where practical.
4. Run the built-in `--preview <folder>` capture and save the resulting images under `docs/screenshots/<version>/`.
5. Inspect the images and correct visible clipping, overlap, or regressions before delivery.
6. Update the terse `CHANGELOG.md`, the linked index/current state in `docs/HISTORY.md`, and the exhaustive `docs/history/<version>.md`. Put last/resulting/next states, challenges, and screenshot links only in the version record. Update `README.md` only with stable user-facing information.
7. Preserve a functional executable at `docs/releases/<version>/MemoryBloom.exe` for meaningful checkpoints.
8. Commit all changes belonging to the current user prompt as one descriptive Git commit. Do not include unrelated workspace changes.

When restarting the installed app during development, use `tools/restart-app.ps1 -Show` rather than launching `MemoryBloom.exe --show` directly. The helper starts the window passively and restores the previously focused window so validation does not leave a flashing taskbar button or interrupt the user's active work.

Firefox tab-to-process relationships are inferred rather than authoritative. Never fabricate a mapping: show unresolved tabs explicitly, retain confidence and observation age, and visually warn when a mapping is stale, uncertain, or has drifted.

Every top-level app mode or page must expose the shared dark **Exit Memory Bloom** context action from its primary/title surface.
