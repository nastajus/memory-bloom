# Memory Bloom Companion

This optional local Firefox extension supplies the authoritative open-tab catalog and event timing that Firefox's recovery file cannot provide promptly. It sends only tab metadata (ID, window, title, URL, last-access time, active/hidden/discarded state) to Memory Bloom's local native host; it makes no network requests.

It does **not** receive Firefox content-process PIDs because the WebExtensions Tabs API does not expose them. Memory Bloom still labels PID placement as inferred and retains observation age/confidence.

For development, open `about:debugging`, choose **This Firefox → Load Temporary Add-on**, and select `manifest.json`. Firefox removes temporary add-ons when it restarts. A persistent release needs Mozilla signing.
