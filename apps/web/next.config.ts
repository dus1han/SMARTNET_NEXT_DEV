import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Let the E2E harness build into its own directory (NEXT_DIST_DIR=.next-e2e), so a dev server it
  // starts can never corrupt the .next of a dev server you already have running. Normal dev/build use
  // the default `.next`.
  distDir: process.env.NEXT_DIST_DIR || ".next",
};

export default nextConfig;
