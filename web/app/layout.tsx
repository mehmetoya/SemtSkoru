import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SemtSkoru",
  description: "İstanbul mahalle yaşam skoru ve karşılaştırma",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
