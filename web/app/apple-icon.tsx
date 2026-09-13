import { ImageResponse } from "next/og";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

// Same mark as app/icon.svg and components/Logo.tsx, rendered as a PNG because iOS
// home-screen icons don't accept SVG.
export default function AppleIcon() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#1d4ed8",
        }}
      >
        <svg width="120" height="120" viewBox="0 0 32 32">
          <path
            d="M13 11.2c0-2.4 2.3-3.7 3.8-3.7 2.1 0 3.5 1.1 3.9 2.6M13 11.2c0 3.1 7.7 1.7 7.7 5.2 0 1.7-1.5 3.1-3.9 3.1-1.6 0-3.5-.6-4-2.7"
            fill="none"
            stroke="#ffffff"
            strokeWidth="2.4"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </div>
    ),
    { ...size },
  );
}
