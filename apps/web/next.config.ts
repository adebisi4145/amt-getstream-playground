import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Hides the floating dev-tools badge during demos. Compile and runtime errors still surface in
  // the terminal and the browser console — this only removes the on-screen indicator.
  devIndicators: false,
};

export default nextConfig;
