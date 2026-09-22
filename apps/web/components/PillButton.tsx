import type { ButtonHTMLAttributes } from "react";

type Variant = "primary" | "white" | "disabled";

const VARIANTS: Record<Variant, string> = {
  // Figma: bg blue-500, radius 32, padding 8/20, label Work Sans Bold 14 white.
  primary: "bg-amt-blue text-white hover:bg-amt-blue/90",
  white: "bg-white text-amt-blue hover:bg-white/90",
  disabled: "bg-amt-grey text-white",
};

interface PillButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
}

export function PillButton({ variant = "primary", className = "", ...props }: PillButtonProps) {
  const style = props.disabled ? VARIANTS.disabled : VARIANTS[variant];

  return (
    <button
      {...props}
      className={`inline-flex items-center justify-center gap-2 rounded-[32px] px-5 py-2 text-sm font-bold transition-colors disabled:cursor-not-allowed ${style} ${className}`}
    />
  );
}
