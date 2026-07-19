import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { Providers } from "./providers";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "INVOSYS",
  description: "Inventory and invoicing",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    // suppressHydrationWarning: next-themes sets the `class` on <html> before React hydrates, so
    // the server's markup and the client's differ by design. It is scoped to this element only.
    <html
      lang="en"
      suppressHydrationWarning
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      {/*
        The window never scrolls; the app does. `.app-canvas` is h-dvh with overflow-hidden and <main>
        carries overflow-y-auto, so scrolling belongs to the content area — but `min-h-full` left the
        document itself scrollable, and anything that made it a pixel taller than the viewport (a
        horizontal scrollbar stealing height, a rounding difference between 100% and 100dvh) produced a
        second scrollbar at the window edge on top of the app's own.

        Pages outside the shell — login, change-password — scroll within their own container instead.
      */}
      <body className="flex h-full flex-col overflow-hidden bg-canvas text-text">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
