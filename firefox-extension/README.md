# Memory Bloom Companion

This optional local Firefox extension supplies the authoritative open-tab catalog and event timing that Firefox's recovery file cannot provide promptly. It sends tab metadata—ID, window, title, URL, last-access time, and active/hidden/discarded state—only to Memory Bloom's native host on the same computer. It makes no network requests.

Mozilla's policy treats data handled outside the extension or browser as transmitted even when the destination is a local native process. The manifest therefore truthfully declares required `browsingActivity`, `websiteContent`, and `websiteActivity` data categories. Firefox shows these disclosures at installation. The package requires Firefox 140 or newer so Firefox's built-in consent interface is available; Memory Bloom does not upload this data or send it off the computer.

The companion is necessary if you want Memory Bloom to know the full current tab catalog promptly. Without it, Bloom can still show Firefox process memory and use recovery/foreground-title observations, but background titles may arrive minutes late or remain unseen. Even with the companion, Firefox does not expose content-process PIDs through the Tabs API; tab identity becomes authoritative, while PID placement remains confidence-scored inference.

## Temporary loading after each Firefox restart

Standard Firefox removes a temporary extension when the browser exits. Each new Firefox session therefore requires:

1. Start Memory Bloom and install its native host once by running `install-firefox-companion-host.ps1` from the project root.
2. In Firefox, enter `about:debugging` in the address bar.
3. Select **This Firefox**.
4. Select **Load Temporary Add-on**.
5. Browse to this folder and select `manifest.json`.

That is normally under a minute once the folder is familiar, but it must be repeated after every Firefox restart. The **Reload** button on `about:debugging` is only for applying extension file changes during the current session; it does not make installation persistent. Mozilla also offers `web-ext run` for development automation, but it launches a development session and has the same temporary/restart limitation, so it is not a clean everyday startup solution.

Official reference: [Temporary installation in Firefox](https://extensionworkshop.com/documentation/develop/temporary-installation-in-firefox/).

## Recommended persistent path: signed but unlisted

An unlisted package is signed by Mozilla but is not publicly searchable on addons.mozilla.org. It is the lowest-friction persistent choice while Memory Bloom remains experimental.

Preparation for this companion should be modest: it already has a stable add-on ID for native messaging, explicit data-handling declarations, readable unminified JavaScript, no bundler/build step, and no network requests. Its API permissions are limited to `tabs` and `nativeMessaging`. The native host installer is still required once because signing installs the Firefox half, not the Windows bridge.

Submission flow:

1. Create or sign in to a Mozilla/AMO developer account and accept the distribution agreement.
2. Package the files in this directory as a ZIP/XPI with `manifest.json` at the archive root.
3. In the AMO Developer Hub, select **Submit a New Add-on** and choose **On your own** (self-distribution/unlisted).
4. Upload the package. AMO runs automated validation; errors must be fixed, while warnings should be reviewed.
5. Select Firefox as the compatible platform and answer the source-code question. Because this package is plain source with no minification or generated bundle, a separate source archive should generally not be necessary; AMO makes the final determination.
6. Submit and wait for signing. Mozilla states that signing/publication can take up to 24 hours, or longer if selected for manual review. Reviewers may request clarification or test instructions.
7. Download the signed `.xpi` from the version page, then install it through **Add-ons and themes → gear menu → Install Add-on From File**.

For later updates, increment the manifest version and submit the new package to the same AMO add-on record. With unlisted self-distribution, either distribute each signed update manually or add and host an `update_url` manifest. A public AMO listing provides Firefox-managed updates but adds public listing metadata and a marketplace presence.

Official references:

- [Submitting an add-on](https://extensionworkshop.com/documentation/publish/submitting-an-add-on/)
- [Signing and distribution overview](https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/)
- [Installing self-distributed extensions](https://extensionworkshop.com/documentation/publish/install-self-distributed/)
- [Source-code submission requirements](https://extensionworkshop.com/documentation/publish/source-code-submission/)
- [Firefox built-in data consent](https://extensionworkshop.com/documentation/develop/firefox-builtin-data-consent/)

## Effort and delay estimate

- **Temporary route:** no account or review; about one minute of manual work per Firefox restart.
- **First unlisted submission:** approximately 30–60 minutes to create the account, package, validate, write reviewer notes, and correct ordinary warnings. This is an estimate for Memory Bloom's small readable package, not a Mozilla guarantee.
- **Mozilla delay:** often automated, but budget up to 24 hours per Mozilla's published guidance; a selected manual review can take longer and has no guaranteed turnaround.
- **Future signed versions:** typically a short repackage/version-bump/upload task, followed by signing. Manual distribution remains work unless an update manifest is hosted.

The practical recommendation is to use temporary loading for immediate testing, verify the live catalog/native bridge for several sessions, then submit an unlisted package once the behavior is stable enough to avoid repeated signed-version churn.
