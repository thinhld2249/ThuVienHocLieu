// Helpers dùng chung cho các trang Admin (M6).

export function formatBytes(bytes: number): string {
  if (bytes >= 1e9)
    return (bytes / 1e9).toLocaleString("vi-VN", { maximumFractionDigits: 1 }) + " GB";
  if (bytes >= 1e6)
    return (bytes / 1e6).toLocaleString("vi-VN", { maximumFractionDigits: 1 }) + " MB";
  if (bytes >= 1e3)
    return (bytes / 1e3).toLocaleString("vi-VN", { maximumFractionDigits: 1 }) + " KB";
  return `${bytes} B`;
}

export const USER_STATUS_LABEL: Record<string, string> = {
  Pending: "Chờ duyệt",
  Active: "Hoạt động",
  Suspended: "Đang khóa",
  Rejected: "Đã từ chối",
};

export const SCOPE_LABEL: Record<string, string> = {
  Public: "Công khai",
  Teachers: "Giáo viên",
  Team: "Tổ",
  Private: "Riêng tư",
};

export const AUDIENCE_LABEL: Record<string, string> = {
  Public: "Công khai",
  Teachers: "Giáo viên",
  Team: "Tổ",
};

/** publishState (enum BE) → nhãn tiếng Việt (spec §4.4). */
export const PUBLISH_STATE_LABEL: Record<string, string> = {
  Visible: "Đang hiện",
  Hidden: "Đang ẩn",
  ScheduledUpcoming: "Sắp mở",
  ScheduledOpen: "Đang mở",
  Closed: "Đã đóng",
};

export const MODERATION_LABEL: Record<string, string> = {
  Approved: "Đã duyệt",
  PendingReview: "Chờ duyệt",
  Rejected: "Trả lại",
};
