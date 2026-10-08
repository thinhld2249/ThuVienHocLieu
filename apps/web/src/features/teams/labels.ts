import type { TeamRole } from "@/features/auth/types";
import type { InvitationResult } from "./types";

/** Vai trò tổ — thuật ngữ thống nhất (spec §14.4). */
export function teamRoleLabel(role: TeamRole | string | null | undefined): string {
  switch (role) {
    case "Lead":
      return "Tổ trưởng";
    case "Deputy":
      return "Tổ phó";
    case "Member":
      return "Thành viên";
    default:
      return "Khách";
  }
}

export function teamRoleVariant(
  role: TeamRole | string | null | undefined,
): "default" | "success" | "outline" {
  if (role === "Lead") return "default";
  if (role === "Deputy") return "success";
  return "outline";
}

/** Status lời mời (bằng tiếng Việt từ BE) → variant Badge. */
export function invitationStatusVariant(
  status: string,
): "default" | "success" | "warning" | "destructive" | "outline" {
  switch (status) {
    case "Chờ":
      return "default";
    case "Đã nhận":
      return "success";
    case "Hết hạn":
      return "warning";
    case "Đã thu hồi":
      return "destructive";
    default:
      return "outline";
  }
}

/** Kết quả mời từng email → nhãn + loại thông báo cho UI. */
export function invitationResultInfo(
  result: InvitationResult,
): { label: string; tone: "ok" | "info" | "warn" } {
  switch (result.action) {
    case "added":
      return { label: "Đã thêm vào tổ", tone: "ok" };
    case "approved":
      return { label: "Đã duyệt và thêm vào tổ", tone: "ok" };
    case "invited":
      return { label: "Đã tạo lời mời", tone: "ok" };
    case "existing":
      return { label: result.reason ?? "Đã là thành viên", tone: "info" };
    case "existing_invite":
      return { label: "Đã có lời mời đang chờ", tone: "info" };
    case "skipped":
      return { label: result.reason ?? "Bỏ qua", tone: "warn" };
    default:
      return { label: result.reason ?? result.action, tone: "info" };
  }
}

/**
 * Tách nhiều email (xuống dòng, phẩy, chấm phẩy) — khớp logic BE
 * (TeamsService.ParseEmails): giữ thứ tự, bỏ trùng (không phân biệt hoa thường).
 */
export function parseEmails(input: string): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const part of input.split(/[\n\r,;]/)) {
    const email = part.trim();
    if (email.includes("@") && email.includes(".") && !seen.has(email.toLowerCase())) {
      seen.add(email.toLowerCase());
      result.push(email);
    }
  }
  return result;
}
