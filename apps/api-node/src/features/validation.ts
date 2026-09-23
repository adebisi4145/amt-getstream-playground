import { z } from "zod";

/**
 * Letters, digits, @, _ and -, up to 255 characters. This is our own conservative rule;
 * Stream doesn't publish its user ID limits.
 */
export const userIdSchema = z
  .string({ error: "The userId field is required." })
  .min(1, "The userId field is required.")
  .max(255, "The userId field must be at most 255 characters.")
  .regex(/^[A-Za-z0-9@_-]+$/, "The userId field may only contain letters, digits, @, _ and -.");

/** Requires an absolute http or https URL. `null` is treated as not sent. */
export const httpUrlSchema = z
  .string()
  .refine((value) => {
    if (!URL.canParse(value)) return false;
    const { protocol } = new URL(value);
    return protocol === "http:" || protocol === "https:";
  }, "The image field must be an absolute http or https URL.");

/** JSON `null` means "not sent", the same as leaving the field out. */
export function optional<T extends z.ZodType>(schema: T) {
  return schema.nullish().transform((value) => value ?? undefined);
}
