import { initials } from "@/lib/demo-users";

interface AvatarProps {
  name: string;
  /** Diameter in pixels. Figma uses 100 for the remote participant and 70 in the PiP tile. */
  size?: number;
  className?: string;
}

/** Circle with initials, as used on the call screen (Geist Medium, white on success-400). */
export function Avatar({ name, size = 100, className = "bg-amt-success" }: AvatarProps) {
  return (
    <div
      className={`flex shrink-0 items-center justify-center rounded-full font-display font-medium text-white ${className}`}
      style={{ width: size, height: size, fontSize: size * 0.32 }}
    >
      {initials(name)}
    </div>
  );
}
