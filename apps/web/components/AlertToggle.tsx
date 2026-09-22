"use client";

import { useState } from "react";
import { MdNotificationsActive, MdNotificationsNone } from "react-icons/md";
import { enableAlerts } from "@/lib/useAlert";

/**
 * Turning alerts on needs a click: browsers block audio until the page has been interacted with,
 * and desktop notifications need explicit permission.
 */
export function AlertToggle() {
  const [enabled, setEnabled] = useState(false);

  if (enabled) {
    return (
      <span className="flex items-center gap-1 text-sm text-amt-black-400">
        <MdNotificationsActive className="text-amt-blue" />
        Alerts on
      </span>
    );
  }

  return (
    <button
      type="button"
      onClick={async () => setEnabled(await enableAlerts())}
      className="flex items-center gap-1 text-sm font-medium text-amt-blue hover:underline"
    >
      <MdNotificationsNone />
      Turn on alerts
    </button>
  );
}
