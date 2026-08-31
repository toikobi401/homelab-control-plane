import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

/**
 * Ghép class Tailwind, class sau thắng class trước khi cùng nhóm thuộc tính.
 * Quy ước của shadcn/ui — mọi component ui đều dùng.
 */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
