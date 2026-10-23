let port = null;
let sessionId = crypto.randomUUID();
let focusedWindowId = browser.windows.WINDOW_ID_NONE;
let publishTimer = null;

async function connect() {
  try {
    port = browser.runtime.connectNative("memory_bloom");
    port.onDisconnect.addListener(() => {
      port = null;
      setTimeout(connect, 3000);
    });
    const focused = await browser.windows.getLastFocused();
    focusedWindowId = focused.id;
    await publish("connected", -1);
  } catch (_) {
    port = null;
    setTimeout(connect, 3000);
  }
}

async function publish(reason, eventTabId) {
  if (!port) return;
  try {
    const tabs = await browser.tabs.query({});
    port.postMessage({
      type: "snapshot",
      sessionId,
      sentAt: Date.now(),
      reason,
      eventTabId,
      focusedWindowId,
      tabs: tabs.map(tab => ({
        id: tab.id,
        windowId: tab.windowId,
        title: tab.title || "Untitled tab",
        url: tab.url || "",
        lastAccessed: tab.lastAccessed || Date.now(),
        hidden: Boolean(tab.hidden),
        active: Boolean(tab.active),
        discarded: Boolean(tab.discarded)
      }))
    });
  } catch (_) {
    // A disconnect callback schedules reconnection.
  }
}

function schedule(reason, tabId = -1, delay = 100) {
  clearTimeout(publishTimer);
  publishTimer = setTimeout(() => publish(reason, tabId), delay);
}

browser.tabs.onActivated.addListener(info => schedule("activated", info.tabId, 0));
browser.tabs.onCreated.addListener(tab => schedule("created", tab.id));
browser.tabs.onRemoved.addListener(tabId => schedule("removed", tabId));
browser.tabs.onReplaced.addListener((addedTabId) => schedule("replaced", addedTabId));
browser.tabs.onAttached.addListener(tabId => schedule("attached", tabId));
browser.tabs.onDetached.addListener(tabId => schedule("detached", tabId));
browser.tabs.onMoved.addListener(tabId => schedule("moved", tabId));
browser.tabs.onUpdated.addListener((tabId, changeInfo) => {
  const important = changeInfo.status === "loading" || "url" in changeInfo || "title" in changeInfo || "discarded" in changeInfo;
  if (important) schedule(changeInfo.status === "loading" ? "loading" : "updated", tabId, 40);
});
browser.windows.onFocusChanged.addListener(windowId => {
  focusedWindowId = windowId;
  schedule("window-focus", -1, 0);
});

setInterval(() => publish("heartbeat", -1), 10000);
connect();
