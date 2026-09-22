"use client";

import { useEffect, useRef } from "react";

/**
 * Gets a busy person's attention while something is waiting for them.
 *
 * Staff don't sit staring at this tab: triage works in other systems, doctors are between
 * consultations. So while `active` is true this rings, flashes the tab title, and (if allowed)
 * raises a desktop notification. All three, because each fails on its own: audio is blocked until
 * the page has been interacted with, notifications need permission, and the title is invisible if
 * the tab is focused.
 */
export function useAlert(active: boolean, title: string, body: string) {
  const audioRef = useRef<AudioContext | null>(null);

  useEffect(() => {
    if (!active) {
      return;
    }

    const originalTitle = document.title;
    let showingAlert = false;

    const flash = window.setInterval(() => {
      showingAlert = !showingAlert;
      document.title = showingAlert ? `🔔 ${title}` : originalTitle;
    }, 1000);

    const ring = () => {
      // Generated rather than shipped as an audio file: it's two short beeps.
      try {
        audioRef.current ??= new AudioContext();
        const context = audioRef.current;

        // Autoplay policy suspends the context until the page has been clicked.
        if (context.state === "suspended") {
          void context.resume();
        }

        [0, 0.3].forEach((offset) => {
          const oscillator = context.createOscillator();
          const gain = context.createGain();

          oscillator.frequency.value = 880;
          gain.gain.setValueAtTime(0.0001, context.currentTime + offset);
          gain.gain.exponentialRampToValueAtTime(0.2, context.currentTime + offset + 0.02);
          gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + offset + 0.22);

          oscillator.connect(gain).connect(context.destination);
          oscillator.start(context.currentTime + offset);
          oscillator.stop(context.currentTime + offset + 0.25);
        });
      } catch {
        // No audio available; the title and notification still do their job.
      }
    };

    ring();
    const repeat = window.setInterval(ring, 3000);

    let notification: Notification | undefined;
    if (typeof Notification !== "undefined" && Notification.permission === "granted") {
      notification = new Notification(title, { body, tag: "amt-consultation" });
    }

    return () => {
      window.clearInterval(flash);
      window.clearInterval(repeat);
      document.title = originalTitle;
      notification?.close();
    };
  }, [active, title, body]);
}

/**
 * Asks for notification permission from a click, which also unblocks audio: browsers keep the
 * audio context suspended until the page has been interacted with.
 */
export async function enableAlerts(): Promise<boolean> {
  try {
    new AudioContext().close();
  } catch {
    // Ignore: this only exists to satisfy the autoplay gesture requirement.
  }

  if (typeof Notification === "undefined") {
    return false;
  }

  if (Notification.permission === "granted") {
    return true;
  }

  return (await Notification.requestPermission()) === "granted";
}
